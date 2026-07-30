using TradingRobot.Domain.Abstractions;
using TradingRobot.MarketData.Binance;
using TradingRobot.StrategyTester.Api.Backtesting;

namespace TradingRobot.StrategyTester.Api.Endpoints;

public static class BacktestEndpoints
{
    public static void MapBacktestEndpoints(this WebApplication app)
    {
        // GET /api/backtest?symbol=BTCUSDT&interval=1h&from=2025-01-01&to=2025-06-01
        // Fetches candles once, then runs every registered IStrategy against the same
        // history and returns one BacktestResult per strategy — this is how "run many
        // strategies simultaneously" is answered for the tester: register more
        // strategies via DI in Program.cs, no endpoint/engine changes needed.
        app.MapGet("/api/backtest", async (
            string symbol, string interval, DateTimeOffset from, DateTimeOffset to,
            BinanceRestClient marketData, IEnumerable<IStrategy> strategies) =>
        {
            var candles = await marketData.GetKlinesAsync(symbol, interval, from, to);
            var engine = new BacktestEngine();
            var results = strategies.Select(strategy => engine.Run(strategy, candles)).ToList();
            return Results.Ok(results);
        })
        .WithName("RunBacktest");
    }
}
