using TradingRobot.Domain.Models;

namespace TradingRobot.Domain.Abstractions;

// Implement this once, run it in three places unmodified:
//   - Strategy Tester: fed historical candles, output judged against future bars.
//   - Signal Generator: fed live candles, Signal becomes a Telegram/email alert.
//   - Live Execution Bot: fed live candles, Signal becomes a real order.
public interface IStrategy
{
    string Name { get; }

    // Called once per new candle. Return null for "no action".
    Signal? OnCandle(Candle candle, IReadOnlyList<Candle> history);
}
