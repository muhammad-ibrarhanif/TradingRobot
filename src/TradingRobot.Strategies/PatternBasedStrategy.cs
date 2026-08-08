using TradingRobot.Domain.Abstractions;
using TradingRobot.Domain.Models;
using TradingRobot.PatternDetection;

namespace TradingRobot.Strategies;

// "Price action can generate signals on its own" — per
// Dashboard-Frontend-Requirements.md "Signal generation — patterns vs indicators
// vs combined." Turns candlestick pattern recognition into an independent signal
// source, separate from chart highlighting: the purple highlight bands are always
// drawn for every detected pattern regardless of which strategies are registered
// here (see MarketDataApiController.GetPatterns, which calls PatternDetector
// directly) — this class only concerns itself with turning a pattern into a
// Buy/Sell call for whoever is listening (SignalWorker, the backtester, etc.).
//
// Deliberately indicator-free: no SMA/RSI/etc. here or anywhere in this class —
// indicators are being paused/deferred per an explicit decision to get price
// action right first (see Dashboard-Frontend-Requirements.md). Trend context
// below is read directly from raw closes, not from any indicator.
public sealed class PatternBasedStrategy(PatternDetector? patternDetector = null) : IStrategy
{
    private readonly PatternDetector _patternDetector = patternDetector ?? new PatternDetector();

    public string Name => "PatternAction";

    public Signal? OnCandle(Candle candle, IReadOnlyList<Candle> history)
    {
        foreach (var match in _patternDetector.DetectLatest(history))
        {
            var side = PatternDirection.For(match.Name);
            if (side is null) continue;

            // Reversal patterns only mean something if there was an actual move
            // in the opposite direction to reverse — a Hammer or Bullish Engulfing
            // shape appearing mid-decline with no preceding downtrend isn't a
            // bottoming signal, it's just noise that happens to match the shape.
            // This was the real cause of a reported "wrong BUY signal": a Hammer
            // fired in the middle of a strong ongoing drop, because the detector
            // (by design — see PatternDetector's own comments) is shape-only with
            // no trend awareness. Checked here with plain price action (recent
            // raw closes), not an indicator.
            if (side == OrderSide.Buy && !HasPriorDowntrend(history)) continue;
            if (side == OrderSide.Sell && !HasPriorUptrend(history)) continue;

            return new Signal(Name, candle.Symbol, side.Value, match.Name, 0.55m, candle.OpenTime);
        }

        return null;
    }

    // Reads the last `lookback` closes *before* the current (pattern) candle and
    // checks whether price was actually trending in the direction a reversal
    // pattern needs to be meaningful. Deliberately simple — first-vs-last close
    // in the window, not a moving average — so this stays "price action," not an
    // indicator calculation.
    private static bool HasPriorDowntrend(IReadOnlyList<Candle> history, int lookback = 5)
    {
        var window = PriorWindow(history, lookback);
        return window is not null && window[^1].Close < window[0].Close;
    }

    private static bool HasPriorUptrend(IReadOnlyList<Candle> history, int lookback = 5)
    {
        var window = PriorWindow(history, lookback);
        return window is not null && window[^1].Close > window[0].Close;
    }

    private static IReadOnlyList<Candle>? PriorWindow(IReadOnlyList<Candle> history, int lookback)
    {
        // history's last entry is the current (pattern) candle itself — the
        // window we want is the `lookback` candles immediately before it.
        var end = history.Count - 1;
        if (end < lookback) return null;
        return history.Skip(end - lookback).Take(lookback).ToList();
    }
}
