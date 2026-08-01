using TradingRobot.Domain.Abstractions;
using TradingRobot.Domain.Models;
using TradingRobot.PatternDetection;

namespace TradingRobot.Strategies;

// "Price action and an indicator can generate a signal together" — per
// Dashboard-Frontend-Requirements.md "Signal generation — patterns vs indicators
// vs combined." A pattern is the trigger; the indicator is read as confirming
// context, not a second independent trigger that has to fire on the exact same
// candle (SmaCrossStrategy's own crossover signal happens far less often than a
// pattern is detected, so requiring both to fire simultaneously would essentially
// never confirm anything). Only produces a signal when a detected pattern's
// direction agrees with the indicator's current trend bias.
public sealed class ConfirmedStrategy(
    PatternDetector? patternDetector = null,
    int fastPeriod = 10,
    int slowPeriod = 30) : IStrategy
{
    private readonly PatternDetector _patternDetector = patternDetector ?? new PatternDetector();

    // Kept short deliberately — this becomes a marker-label prefix on the chart
    // once more than one strategy is active (see dashboard.js buildSignalMarkers),
    // and "Confirmed(Pattern+SmaCross(10,30)): SELL" was unreadable/overlapping in
    // practice. The fast/slow periods still show up in the Reason text below.
    public string Name => "Confirmed";

    public Signal? OnCandle(Candle candle, IReadOnlyList<Candle> history)
    {
        var bias = SmaCrossStrategy.CurrentBias(history, fastPeriod, slowPeriod);
        if (bias is null) return null;

        foreach (var match in _patternDetector.DetectLatest(history))
        {
            var side = PatternDirection.For(match.Name);
            if (side == bias)
                return new Signal(Name, candle.Symbol, side.Value, $"{match.Name} confirmed by SMA({fastPeriod},{slowPeriod}) trend", 0.8m, candle.OpenTime);
        }

        return null;
    }
}
