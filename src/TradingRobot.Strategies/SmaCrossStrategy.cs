using TradingRobot.Domain.Abstractions;
using TradingRobot.Domain.Models;

namespace TradingRobot.Strategies;

// Moved here from TradingRobot.StrategyTester.Api so every service — the tester,
// the Signal Generator, the Live Execution Bot, and now Dashboard.Web's historical
// signal computation — references the same implementation instead of each keeping
// its own copy. This is the concrete form of "one IStrategy runs everywhere."
//
// Fast/slow SMA crossover: golden cross -> Buy signal, death cross -> Sell signal.
public sealed class SmaCrossStrategy(int fastPeriod = 10, int slowPeriod = 30) : IStrategy
{
    public string Name => $"SmaCross({fastPeriod},{slowPeriod})";

    public Signal? OnCandle(Candle candle, IReadOnlyList<Candle> history)
    {
        if (history.Count < slowPeriod + 1) return null;

        var closes = history.Select(c => c.Close).ToList();
        var fastNow = closes.TakeLast(fastPeriod).Average();
        var slowNow = closes.TakeLast(slowPeriod).Average();
        var fastPrev = closes.SkipLast(1).TakeLast(fastPeriod).Average();
        var slowPrev = closes.SkipLast(1).TakeLast(slowPeriod).Average();

        if (fastPrev <= slowPrev && fastNow > slowNow)
            return new Signal(Name, candle.Symbol, OrderSide.Buy, "Golden cross", 0.6m, candle.OpenTime);

        if (fastPrev >= slowPrev && fastNow < slowNow)
            return new Signal(Name, candle.Symbol, OrderSide.Sell, "Death cross", 0.6m, candle.OpenTime);

        return null;
    }
}
