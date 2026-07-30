using TradingRobot.Domain.Abstractions;
using TradingRobot.Domain.Models;

namespace TradingRobot.Strategies;

// Runs one or more strategies candle-by-candle over a fixed historical list and
// collects every Signal produced, in chronological order. This is the "just show
// me what a strategy would have flagged over this date range" building block —
// simpler than BacktestEngine (no equity/trade simulation, just raw signals),
// used by Dashboard.Web to compute signal markers for a chosen historical range
// where SignalGenerator.Worker's live Redis Stream has no data.
public static class HistoricalSignalRunner
{
    public static IReadOnlyList<Signal> Run(IEnumerable<IStrategy> strategies, IReadOnlyList<Candle> candles)
    {
        var signals = new List<Signal>();
        var history = new List<Candle>();
        var strategyList = strategies.ToList();

        foreach (var candle in candles)
        {
            history.Add(candle);
            foreach (var strategy in strategyList)
            {
                var signal = strategy.OnCandle(candle, history);
                if (signal is not null) signals.Add(signal);
            }
        }

        return signals;
    }
}
