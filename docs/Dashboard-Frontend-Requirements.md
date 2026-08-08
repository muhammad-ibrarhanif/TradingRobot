# Dashboard.Web Frontend — Locked Requirements (v1)

## Goal

Turn `TradingRobot.Dashboard.Web` from a landing-page stub into a TradingView-style market structure viewer: multi-timeframe candlestick charts, a symbol watchlist, manual drawing tools, and indicator overlays, backed by real Binance data.

## Architecture

**Pattern:** ASP.NET Core MVC (Controllers + Razor Views), not Blazor and not a separate TypeScript/SPA build.

- Controllers pass initial state (default symbol, available timeframes, watchlist) into `.cshtml` views via the model.
- Views use Razor for layout/partials (nav, watchlist sidebar, chart panel) — standard server-rendered HTML.
- Actual chart rendering is plain JavaScript (no build step, no TypeScript) using **TradingView Lightweight Charts**, the same library already used in the Strategy Tester prototype. Scripts live in `wwwroot/js` and are referenced directly from views.
- Live/updated data reaches the browser via a JSON API exposed by Dashboard.Web (polling or a WebSocket/SignalR relay — implementation detail to decide during build), which in turn reads from Redis (cache) and the market data layer below. The browser does not call Binance directly.

## Data layer — decision change

**Binance.Net** (the community `Binance.Net` NuGet package by JKorf) replaces the hand-rolled `TradingRobot.MarketData.Binance` REST/WebSocket client for this work. Rationale: a maintained client covering the full Binance API surface (auth, error handling, rate limiting, reconnect logic) is more reliable than continuing to hand-write REST calls and raw WebSocket parsing.

Impact on the existing scaffold:
- `TradingRobot.MarketData.Binance` (custom `BinanceRestClient` / `BinanceWebSocketClient`) is superseded for the dashboard's data needs. Whether `StrategyTester.Api` and `LiveExecutionBot.Worker` also migrate to Binance.Net, or keep the hand-rolled client, is an open follow-up decision — not required to unblock this frontend work.
- `IMarketDataProvider` in `TradingRobot.Domain` stays as the abstraction; a new implementation wraps Binance.Net instead of the custom client.

## v1 feature scope

All four selected, with layout locked as follows:

1. **Multi-timeframe candlestick chart, single symbol** — live Binance candles, timeframe switcher (1m/5m/1h/1d/etc.) as a row of buttons in the top bar. The baseline viewing experience everything else sits on top of.
2. **Symbol switching** — a **dynamic dropdown** in the top bar (not a static sidebar watchlist), populated from the available Binance symbols. Selecting one swaps the active chart's data feed.
3. **Drawing tools** (trendlines, horizontal levels) — live in a **vertical toolbar on the left edge** of the chart (cursor, trendline, horizontal ray, arrow, rectangle, eraser), not a bottom bar. Lightweight Charts has no built-in drawing toolkit, so this is hand-built: canvas/SVG overlay for user-drawn annotations, position-synced to the chart's time/price scale, persisted per symbol (storage mechanism TBD — browser-side to start).
4. **Indicators** — a dedicated **"Indicators" control in the top bar** (button/dropdown to add SMA/EMA/RSI/etc. overlays onto the chart). The control and its plumbing (compute + render) are being deferred to a later build pass, but the UI needs a placeholder section reserved for it now so the layout doesn't need reshuffling when it's built.
5. **Buy/sell signal markers, from multiple concurrent strategies** — the chart plots arrows on candles where a running strategy emitted a `Signal` (green arrow-up/"BUY" below the bar, red arrow-down/"SELL" above the bar), same visual language already used for trade markers in the Strategy Tester's backtest chart. No single strategy has been defined/validated yet, and the system is designed to run several at once (see "Multi-strategy support" below), so each marker's label includes the strategy name (`Signal.StrategyName`) and each strategy gets a distinct marker color, so overlapping signals from different strategies stay legible rather than blurring into one undifferentiated "the system says buy" mark. Source: rather than Dashboard.Web running its own copy of every strategy against the live feed, it reads the signals that `SignalGenerator.Worker` already computes and publishes to Redis (see "Signal transport" below) — one computation feeds both the Telegram/email alert and this chart marker, keeping "one `IStrategy` runs everywhere" true rather than adding a fourth place that evaluates it.
6. **Candlestick pattern highlighting** — classic price-action patterns (doji, hammer, shooting star, bullish/bearish engulfing, morning/evening star, etc.) are detected directly from OHLC data and visually called out on the chart, styled distinctly from the buy/sell arrows in item 5 so the two aren't confused at a glance. Purpose is explicitly to make price action easier to read by eye, not just to feed signals. **The legend/UI must label these explicitly as "pattern" vs. "signal"** (e.g. a small "Pattern" tag on the purple band, "Signal" on the arrows) — not just different colors — so a user glancing at the chart can't mistake "here's a shape a human would recognize" for "the system found a trade."
   - **Every candle that participates in the pattern gets highlighted, not just one.** A single-candle pattern (doji, hammer) highlights that one candle. A multi-candle pattern (engulfing = 2 candles, morning/evening star = 3 candles) highlights the full span — a background band/box drawn behind all involved candles, with one label for the pattern name (not one badge per candle) so it reads as "these N candles together form X," matching how a trader would actually spot it.
   - Architecture: a shared pattern-detection library (new, e.g. `TradingRobot.PatternDetection`, taking `IReadOnlyList<Candle>` and returning, per detected pattern, the pattern name plus the *range of candle indices/timestamps involved*) sits alongside `IStrategy` in the reusable core — the chart consumes it for visual highlighting, and it doubles as an optional input a strategy can use to actually generate `Signal`s (a pattern-based strategy is a natural next `IStrategy` implementation, not required for this to ship).

## Multi-strategy support (locked)

No strategy has been defined/validated yet, and the design requirement going forward is to build and run **many strategies simultaneously**, not one hardcoded strategy. This is now wired into the backend, not just planned:

- `Signal` (in `TradingRobot.Domain`) carries a `StrategyName` field so signals from different strategies are distinguishable downstream.
- `SignalGenerator.Worker` and `LiveExecutionBot.Worker` both resolve `IEnumerable<IStrategy>` via DI instead of a single `IStrategy` — every strategy registered in each service's `Program.cs` runs independently against the same candle stream, no code changes needed to add or remove one.
- `StrategyTester.Api`'s `/api/backtest` endpoint runs every registered strategy against the same historical data and returns one result per strategy, so the tester answers "run and compare many strategies" directly.
- **Risk note carried over from the Live Execution Bot's own docs:** fan-out is safe for the Signal Generator (more alerts) but not automatically safe for live execution (multiple strategies independently sizing and placing orders with no shared capital/exposure coordination). Only the Signal Generator and Strategy Tester should run multiple strategies today; the Live Execution Bot stays single-strategy until a portfolio/allocation layer exists.

## Signal generation — patterns vs indicators vs combined (locked)

Mirrors how a human actually trades off a chart: always watching for candlestick patterns as they form, optionally layering an indicator on top for confirmation — rather than the indicator being an independent, always-on trigger. Three signal sources exist, all registered as ordinary `IStrategy` implementations (`TradingRobot.Strategies`) and able to run simultaneously, exactly like any other multi-strategy setup:

- **`PatternBasedStrategy`** — price action alone. Wraps `PatternDetector.DetectLatest` (a cheap "just check the newest candle" variant of `Detect`, added so per-candle strategy evaluation stays O(1) instead of rescanning the whole history every tick). Directional patterns (Hammer, Bullish engulfing → Buy; Shooting star, Bearish engulfing → Sell) fire their own signal immediately; Doji is intentionally excluded — it signals indecision, not a direction — and stays highlight-only. The name-to-direction mapping lives in `PatternDetection.PatternDirection`, shared so nothing else has to duplicate it.
- **`SmaCrossStrategy`** — an indicator alone, unchanged from before (golden/death cross).
- **`ConfirmedStrategy`** — both together. A pattern is the trigger; `SmaCrossStrategy.CurrentBias` (new: reports fast-vs-slow SMA's *current* relationship, not just crossover events) is read as confirming context. Only fires when a detected pattern's direction agrees with the indicator's present trend bias. Requiring the indicator's own crossover to happen on the exact same candle as a pattern was considered and rejected — crossovers and patterns occur at very different frequencies, so that would almost never confirm anything.

**Pattern highlighting is a separate, always-on layer, independent of which of the above are registered.** `MarketDataApiController.GetPatterns` calls `PatternDetector.Detect` directly for the chart's purple highlight bands — a pattern gets highlighted whether or not `PatternBasedStrategy`/`ConfirmedStrategy` are even enabled, matching "we always need to highlight the patterns regardless of what strategy is going to execute."

**Still hardcoded, not yet end-user-configurable:** which of the three run is decided by the `AddSingleton<IStrategy>(...)` lines in each service's `Program.cs` — a developer choice today, not something the end user toggles from the dashboard without a redeploy. Moving that to a runtime-configurable toggle list (simplest tier discussed) is a deliberately separate, not-yet-started piece of work from the three strategies themselves.

**Decision: Strategy Tester lets you pick which strategy/strategies to test, not "always all" or "mirror live."** Considered tying the tester to the same enabled/disabled toggle as live signals, but the actual requirement is different: test one strategy on its own, or a specific subset, independent of whatever's currently active for live signal generation. Implemented as an optional `strategies` query param on `/api/backtest` (comma-separated `IStrategy.Name` values — omit it to fall back to running everything registered, same as before) plus a new `GET /api/strategies` endpoint the tester's UI (`wwwroot/index.html`) uses to render a checkbox per registered strategy, all checked by default. Also fixed the same server-local-offset date-parsing issue here as `MarketDataApiController` (`from`/`to` now parsed explicitly as UTC).

**Decision: indicators paused, price action first.** `SmaCrossStrategy` and `ConfirmedStrategy` are commented out (not deleted) in all three services' `Program.cs` — only `PatternBasedStrategy` is active right now. Re-enabling the other two later is a one-line change per service, not a rebuild.

**Bug found and fixed: a "wrong BUY signal" on a 1h chart.** Diagnosis: on a single day of 1h candles (24 bars), neither `SmaCrossStrategy` nor `ConfirmedStrategy` had the 30-31 candles of warm-up they need, so the signal had to be `PatternBasedStrategy` matching a Hammer shape purely on its geometry (small body, long lower wick) with no awareness of the surrounding trend — exactly the limitation `PatternDetector`'s own comments already called out ("shape-based only — no trend context... a known simplification worth revisiting once this feeds anything beyond visual highlighting"). It had fired mid-decline, right before price kept dropping — not a real bottoming signal. Fixed in `PatternBasedStrategy` with a plain price-action trend check (first-vs-last close over the last 5 candles *before* the pattern candle, not a moving average): a Buy-side pattern (Hammer, Bullish Engulfing) only fires if that window shows a prior downtrend; a Sell-side pattern (Shooting Star, Bearish Engulfing) only fires if it shows a prior uptrend. Deliberately kept indicator-free per the "price action first" decision above.

## Signal transport (locked): Redis Streams, not pub/sub

Decision: `SignalGenerator.Worker` publishes each `Signal` to a **Redis Stream** (one stream per symbol, e.g. `signals:BTCUSDT`), not a pub/sub channel. Reasoning: pub/sub is fire-and-forget — a browser tab that isn't open at the exact moment a signal fires misses it permanently. A Stream retains recent history, so Dashboard.Web can read the last N entries to backfill signal markers when a chart is opened mid-session, then keep tailing the stream for new ones. This is the best-practice choice for "at least one consumer might not be listening right when the event happens," which describes a browser tab well.

## Chart layout (locked)

```
┌─────────────────────────────────────────────────────────────────┐
│ [Symbol ▾]  [Binance]   [1m][5m][1h][1d]        [Indicators +]  │  <- top bar
├───┬─────────────────────────────────────────────────────────┬───┤
│ ↖ │                                                         │   │
│ ─ │                                                         │ p │
│ ╱ │                   candlestick chart                     │ r │  <- right edge:
│ ▭ │                                                         │ i │     price axis
│ ⌫ │                                                         │ c │     (build later)
│   │                                                         │ e │
├───┴─────────────────────────────────────────────────────────┴───┤
│                         time axis (build later)                  │
└─────────────────────────────────────────────────────────────────┘
```

- **Left**: vertical drawing-tools bar.
- **Right / bottom axes** (time on bottom, price on right): standard chart-axis chrome — deferred to a later build pass, called out explicitly so it isn't forgotten, not because it's optional long-term.
- **Top bar**: symbol dropdown, timeframe buttons, indicators control.

## Sequencing: this build pass vs. the MVP milestone

This document's v1 scope (items 1–4 above) is charting/market-structure only — that's the immediate build pass. It is **not** the finish line.

The **Live Execution Bot status panel** (positions/orders) is confirmed as part of the overall MVP, not dropped — it ships as the next addition to `Dashboard.Web` right after the charting pass lands, so that "minimal viable product" means chart + live status together, not chart alone. Treat the charting work as done when it's solid enough to build the status panel on top of without rework, not as a standalone deliverable to hand off and move on from.

## Explicitly out of scope (even for the MVP milestone)

- Automated trade execution from the chart UI.
- Multi-exchange support — Binance only, consistent with the rest of the solution.

## Build status

This v1 charting pass is now implemented, not just planned. What exists:

- `TradingRobot.MarketData.BinanceNet` (new project) — `BinanceNetMarketDataProvider` implements `IMarketDataProvider` and a Dashboard-only `ISymbolCatalog` via the Binance.Net package. **Package version and some Binance.Net API call shapes are unverified** — written without NuGet access, so expect to fix method/property names against whatever version actually restores locally, same as happened with the original Aspire package versions.
- `TradingRobot.PatternDetection` (new project) — curated v1 pattern set locked in: doji, hammer, shooting star, bullish engulfing, bearish engulfing. Shape-based only, no trend context.
- `Dashboard.Web` converted from a minimal-API stub to ASP.NET Core MVC: `DashboardController` renders the chart view; `MarketDataApiController` exposes `/api/marketdata/{symbols,candles,patterns,signals}`; `CandleHub` (SignalR, mounted at `/hubs/candles`) pushes live closed candles per-connection via a client-driven `SubscribeToSymbol(symbol, interval)` call — locking the "polling vs SignalR" open item in favor of SignalR.
- `SignalGenerator.Worker`'s `SignalWorker` now actually publishes each `Signal` to its per-symbol Redis Stream (`XADD signals:{symbol} data <json>`) — this was a dangling `TODO` before, now implemented, matching the wire format `GetSignals` reads. It's also running the real `SmaCrossStrategy` now instead of the inert placeholder — chosen specifically to validate the pipeline end to end, not because it's been vetted as a good strategy.
- The Razor view/JS implement the full locked layout: dynamic symbol dropdown, timeframe buttons, left drawing-tools bar (cursor/trendline/horizontal-level/eraser, drawn on a canvas overlaid on the chart), the "Indicators" control present but disabled/labeled "coming soon" per the deferred scope, and an explicit "Signal" vs "Pattern" legend/visual distinction (native chart markers for signals, a translucent canvas band for patterns) — not just color-coded.
- **`TradingRobot.Strategies`** (new shared project) — `SmaCrossStrategy` moved here from `StrategyTester.Api` so every service (tester, Signal Generator, and now Dashboard.Web's historical view) references the same implementation, reinforcing "one `IStrategy` runs everywhere" rather than each service keeping its own copy.
- **Date range picker (live vs. historical mode)**: the top bar now has From/To date inputs plus a "Load range"/"Live" toggle. Live mode is unchanged (last 300 candles, SignalR updates, signals from the Redis Stream). Historical mode fetches an explicit `from`/`to` range for candles and patterns, and computes signals on demand via `HistoricalSignalRunner` (new, in `TradingRobot.Strategies`) running the same registered strategies against that range — since the Redis Stream only has recent live signals, nothing for past dates. Historical mode pauses the SignalR subscription (`CandleHub.Unsubscribe()`) rather than disconnecting, so switching back to "Live" is instant.

## Remaining open items

- **Drawing-tool persistence**: trendlines/horizontal levels currently live only in an in-memory JS array — refresh the page and they're gone. Needs a decision (browser `localStorage` to start, per the original open item) and implementation.
- **Binance.Net scope**: only `Dashboard.Web` uses it today. Whether `StrategyTester.Api` and `LiveExecutionBot.Worker` migrate too, to avoid maintaining two separate Binance integrations long-term, is still open.
- **Verify the Binance.Net integration against the real package** once restored locally — flagged as the most likely source of build errors in the previous round, though it turned out to compile as written against whatever version actually restored. Worth a rebuild check again after this round's changes.
- Indicator overlays (item 4) remain deferred as designed — the UI slot exists, the compute/render logic doesn't yet.
- **Date-only parsing nuance**: the From/To inputs send plain `YYYY-MM-DD` values; server-side `DateTimeOffset` parsing of a bare date assumes midnight in the server's local offset, which can shift which candles land in "day N" by a few hours depending on time zone. Fine for now, worth tightening if exact day boundaries start mattering.
  - **Related bug, now fixed**: taking `to` literally as midnight-at-the-start-of-that-day made same-day ranges (e.g. From = To = 01-07-2026) zero-width — the API could only return the single candle sitting at that exact instant, which is what surfaced as "replay only shows 1 candle." `MarketDataApiController` now pushes `to` to the start of the next day (`InclusiveEndOfDay`) before querying, in both `ResolveRange` (candles/patterns) and `GetSignals`' historical branch, so a date-only `to` means "through the end of that calendar day."
  - **Second related bug, now fixed**: `from`/`to` were bound as `DateTimeOffset?` directly, so ASP.NET Core parsed a bare `"2026-07-01"` using the *server's local offset* rather than UTC — on a UTC+1 server, for example, "day 1" actually started at 23:00 UTC the previous day. Binance's own candle timestamps are UTC, so this silently shifted which candles counted as "day 1" depending purely on server timezone. Fixed by binding `from`/`to` as raw strings and parsing them explicitly with `DateTimeStyles.AssumeUniversal | AdjustToUniversal` (`ParseUtcDate`), so a date-only picker value always means UTC midnight regardless of where Dashboard.Web is hosted.
  - **Third related bug, now fixed — this was the real cause of "wrong signals"**: `BinanceNetMarketDataProvider.GetHistoricalCandlesAsync` passed a hardcoded `limit: 1000` straight through to Binance's klines endpoint, which hard-caps every response at 1000 rows regardless of the requested range. A full day of 1m candles is 1440, so anything past the first 1000 minutes was silently missing — visible as the replay progress counter capping at "168/1000" instead of covering the full day. Signals themselves were computed correctly for the candles that *were* present; the strategy just never saw candles past that first ~16.7 hours. Fixed by paging through in 1000-candle chunks, advancing the cursor past the last candle returned each call, until the full range is covered.
  - **Bug found and fixed: pattern highlighting was technically always-on but effectively invisible.** The band was drawn as a translucent column spanning the *entire chart height* (top to bottom) for every detected pattern, which read as a faint vertical smear in the background rather than "this candle is highlighted" — bumping opacity alone didn't fix the actual problem. Fixed properly: `GetPatterns` now returns each pattern's High/Low across the candles it spans, and `drawPatterns()` boxes just that price range (with small padding) instead of the full chart height — a tight highlighted box around the specific bar(s), the way a real pattern-recognition overlay should look, rather than a full-height tint.

**Not a bug, but worth knowing**: `SmaCross(10,30)` is a lagging indicator by design — a buy/sell signal is expected to trail the actual price turn by however long it takes the 10/30-period averages to cross, not fire exactly at the visual high/low. If a signal still looks off after these fixes, the useful question is "did a 10-vs-30 SMA crossover actually happen at that candle," not "does this line up with where I'd have called the turn by eye."

## Fixes from the first real browser verification pass

- **Signal marker labels were unreadable on tighter timeframes**: every marker showed the full `"SmaCross(10,30): BUY"` even with only one strategy running, and on 5m candles the labels overlapped each other and the wicks. Fixed: the strategy-name prefix is now only added when more than one distinct strategy is actually present in the current signal set — with one strategy running, markers just say "BUY"/"SELL". Separately worth knowing: `SmaCross(10,30)` genuinely whipsaws a lot on 5m bars (many quick buy/sell flips) — some of the visual crowding is the strategy being noisy on short timeframes, not purely a UI bug, consistent with it being chosen to validate the pipeline rather than because it's a good strategy.
- **No live signal ever fired**: this wasn't just "not enough time passed" — `SignalWorker` started every run with a completely empty history buffer, and `SmaCrossStrategy` needs 31 closed candles (slow period + 1) before it can evaluate a single crossover. At the default 5-minute interval that's ~2.5 hours of continuous runtime after every restart before a signal is even possible. Fixed: `SignalWorker` now preloads the last 60 candles via `BinanceRestClient` (REST) before starting the live WebSocket stream, so strategies have a working history buffer from the moment the service starts.

## Historical replay (candle-by-candle playback)

Rather than dumping an entire historical range onto the chart instantly, the historical mode now fetches the whole range once (for efficiency) and then plays it back one candle at a time, so patterns and signals only appear once their triggering candle has actually been "reached" — closer to how it would have looked watched live.

- **Data flow**: `loadHistoricalReplayData()` still makes exactly one request each for candles/patterns/signals over the full From/To range (via `Promise.all`), same endpoints as before. Nothing extra is fetched per tick — the whole dataset is already in memory; only the *reveal* is paced.
- **Playback**: `stepReplay()` runs on a `setInterval` (default 150ms/candle) that pushes one more candle onto the chart, then filters `replayPatterns`/`replaySignals` down to whatever is `<=` the current candle's close time and redraws markers/overlay. Speed is adjustable (Slow/Normal/Fast — 400/150/40 ms per candle) via the new replay toolbar.
- **Controls** (`.replay-controls`, shown only in historical mode, hidden in live mode): Play/Pause toggle, speed dropdown, "Skip to end" (jumps straight to the full dataset without waiting out the interval), and a progress counter (`n/total`).
- **Live vs. historical code paths kept separate**: `loadLiveCandles/Patterns/Signals` are untouched from before; the new `loadHistoricalReplayData`/`stepReplay`/`startReplay`/`pauseReplay`/`skipReplayToEnd` are historical-only. A shared `buildSignalMarkers()` helper was extracted so marker styling/coloring logic isn't duplicated between the two paths. `refreshAll()` branches on whether `historicalRange` is set.
- **Entering/leaving historical mode**: switching to a date range calls `unsubscribeLive()` (via the new `CandleHub.Unsubscribe()` hub method) so the live WebSocket feed doesn't keep pushing candles in behind the replay; switching back to "Live" reverses this.
- **Known scaling caveat, not yet addressed**: for a very long range at a fine interval (e.g. a full month of 1m candles), `stepReplay()` calls `setMarkers()` on every tick — fine for a day-sized range, but could get janky over tens of thousands of candles. "Skip to end" is the escape hatch for now; if long-range replay turns out to matter, worth throttling marker redraws (e.g. only on pattern/signal change, not every candle).
