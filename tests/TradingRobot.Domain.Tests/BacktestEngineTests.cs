using TradingRobot.Domain.Models;
using TradingRobot.StrategyTester.Api.Backtesting;
using TradingRobot.StrategyTester.Api.Strategies;
using Xunit;

namespace TradingRobot.Domain.Tests;

public class BacktestEngineTests
{
    [Fact]
    public void Run_WithNoTrades_ReturnsStartingEquityUnchanged()
    {
        var candles = Enumerable.Range(0, 5)
            .Select(i => new Candle("BTCUSDT", "1h", DateTimeOffset.UtcNow.AddHours(i), 100, 100, 100, 100, 1))
            .ToList();

        var result = new BacktestEngine().Run(new SmaCrossStrategy(2, 3), candles, 10_000m);

        Assert.Equal(10_000m, result.EndingEquity);
        Assert.Empty(result.Trades);
    }

    [Fact]
    public void Run_OnGoldenCross_RecordsBuyTrade()
    {
        // Pattern: flat at 100, then drop to 50 (fast dips below slow), then spike to 200
        // (fast crosses back above slow = golden cross → Buy signal).
        static Candle MakeCandle(int i, decimal price) =>
            new("BTCUSDT", "1h", DateTimeOffset.UtcNow.AddHours(i), price, price, price, price, 1);

        var candles =
            Enumerable.Range(0, 15).Select(i => MakeCandle(i, 100m))     // flat — fills slow SMA window
            .Concat(Enumerable.Range(15, 5).Select(i => MakeCandle(i, 50m)))   // drop — fast falls below slow
            .Concat(Enumerable.Range(20, 15).Select(i => MakeCandle(i, 200m))) // spike — fast crosses above slow
            .ToList();

        var result = new BacktestEngine().Run(new SmaCrossStrategy(3, 10), candles, 10_000m);

        Assert.Contains(result.Trades, t => t.Side == OrderSide.Buy);
    }
}
