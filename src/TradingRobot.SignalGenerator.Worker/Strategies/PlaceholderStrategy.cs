using TradingRobot.Domain.Abstractions;
using TradingRobot.Domain.Models;

namespace TradingRobot.SignalGenerator.Worker.Strategies;

// Placeholder so the worker runs out of the box. Replace with a real IStrategy
// (ideally shared from a common strategies library — see TradingRobot.StrategyTester.Api.Strategies)
// once a backtested strategy is ready to go live for alerting.
public sealed class PlaceholderStrategy : IStrategy
{
    public string Name => "Placeholder (always returns null)";
    public Signal? OnCandle(Candle candle, IReadOnlyList<Candle> history) => null;
}
