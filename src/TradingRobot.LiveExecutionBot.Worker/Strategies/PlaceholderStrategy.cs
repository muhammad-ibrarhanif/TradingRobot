using TradingRobot.Domain.Abstractions;
using TradingRobot.Domain.Models;

namespace TradingRobot.LiveExecutionBot.Worker.Strategies;

// Deliberately inert — never returns a signal — so this worker is a no-op
// until a real, backtested strategy is wired in on purpose.
public sealed class PlaceholderStrategy : IStrategy
{
    public string Name => "Placeholder (always returns null)";
    public Signal? OnCandle(Candle candle, IReadOnlyList<Candle> history) => null;
}
