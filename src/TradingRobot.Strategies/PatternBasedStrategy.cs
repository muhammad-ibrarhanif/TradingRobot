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
public sealed class PatternBasedStrategy(PatternDetector? patternDetector = null) : IStrategy
{
    private readonly PatternDetector _patternDetector = patternDetector ?? new PatternDetector();

    public string Name => "PatternAction";

    public Signal? OnCandle(Candle candle, IReadOnlyList<Candle> history)
    {
        foreach (var match in _patternDetector.DetectLatest(history))
        {
            var side = PatternDirection.For(match.Name);
            if (side is not null)
                return new Signal(Name, candle.Symbol, side.Value, match.Name, 0.55m, candle.OpenTime);
        }

        return null;
    }
}
