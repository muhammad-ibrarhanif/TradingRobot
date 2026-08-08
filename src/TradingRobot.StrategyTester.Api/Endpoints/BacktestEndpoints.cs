using System.Globalization;
using TradingRobot.Domain.Abstractions;
using TradingRobot.MarketData.Binance;
using TradingRobot.StrategyTester.Api.Backtesting;

namespace TradingRobot.StrategyTester.Api.Endpoints;

public static class BacktestEndpoints
{
    public static void MapBacktestEndpoints(this WebApplication app)
    {
        // GET /api/strategies — lists every strategy registered in this service, so
        // the tester's UI can render a checkbox per strategy without hardcoding names.
        app.MapGet("/api/strategies", (IEnumerable<IStrategy> strategies) =>
            Results.Ok(strategies.Select(s => s.Name)))
            .WithName("ListStrategies");

        // GET /api/backtest?symbol=BTCUSDT&interval=1h&from=2025-01-01&to=2025-06-01
        // GET /api/backtest?...&strategies=PatternAction,SmaCross(10,30)
        // Fetches candles once, then runs the requested strategies against the same
        // history and returns one BacktestResult per strategy.
        //
        // `strategies` (comma-separated names matching IStrategy.Name) is optional —
        // omit it to compare everything registered, same as before. Provide it to
        // test one strategy on its own or a specific subset at a time, independent of
        // whatever's toggled on for live signal generation elsewhere — the tester's
        // job is "let me try things," not "mirror what's live right now."
        app.MapGet("/api/backtest", async (
            string symbol, string interval, string from, string to,
            string? strategies,
            BinanceRestClient marketData, IEnumerable<IStrategy> allStrategies) =>
        {
            var selected = string.IsNullOrWhiteSpace(strategies)
                ? allStrategies
                : FilterByName(allStrategies, strategies);

            var selectedList = selected.ToList();
            if (selectedList.Count == 0)
                return Results.BadRequest("No matching strategy found for the requested `strategies` list.");

            var candles = await marketData.GetKlinesAsync(symbol, interval, ParseUtcDate(from), ParseUtcDate(to));
            var engine = new BacktestEngine();
            var results = selectedList.Select(strategy => engine.Run(strategy, candles)).ToList();
            return Results.Ok(results);
        })
        .WithName("RunBacktest");
    }

    private static IEnumerable<IStrategy> FilterByName(IEnumerable<IStrategy> strategies, string requested)
    {
        var names = requested.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return strategies.Where(s => names.Contains(s.Name));
    }

    // Same fix as MarketDataApiController.ParseUtcDate: binding `from`/`to` as
    // DateTimeOffset directly parses a bare date using the server's local offset,
    // not UTC, which silently shifts which candles count as "day N" depending on
    // server timezone. Parsing explicitly as UTC keeps this consistent with
    // Binance's own UTC candle timestamps regardless of where this is hosted.
    private static DateTimeOffset ParseUtcDate(string value)
    {
        var dt = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        return new DateTimeOffset(dt, TimeSpan.Zero);
    }
}
