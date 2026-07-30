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
