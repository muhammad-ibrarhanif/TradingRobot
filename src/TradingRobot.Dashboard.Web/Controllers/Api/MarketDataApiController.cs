using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using TradingRobot.Dashboard.Web.Models;
using TradingRobot.Domain.Abstractions;
using TradingRobot.Domain.Models;
using TradingRobot.MarketData.BinanceNet;
using TradingRobot.PatternDetection;
using TradingRobot.Strategies;

namespace TradingRobot.Dashboard.Web.Controllers.Api;

[ApiController]
[Route("api/marketdata")]
public sealed class MarketDataApiController(
    IMarketDataProvider marketData,
    ISymbolCatalog symbolCatalog,
    PatternDetector patternDetector,
    IEnumerable<IStrategy> strategies,
    IConnectionMultiplexer redis) : ControllerBase
{
    // GET /api/marketdata/symbols — powers the top-bar symbol dropdown.
    [HttpGet("symbols")]
    public async Task<IActionResult> GetSymbols(CancellationToken ct)
    {
        var symbols = await symbolCatalog.GetAvailableSymbolsAsync(ct);
        return Ok(symbols);
    }

    // GET /api/marketdata/candles?symbol=BTCUSDT&interval=1h&limit=300
    // GET /api/marketdata/candles?symbol=BTCUSDT&interval=1h&from=2025-03-01&to=2025-03-10
    // `from`/`to` (the date range picker) take priority when present; `limit`
    // ("last N candles up to now") is the fallback for live/default viewing.
    [HttpGet("candles")]
    public async Task<IActionResult> GetCandles(
        [FromQuery] string symbol, [FromQuery] string interval, [FromQuery] int limit,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        CancellationToken ct)
    {
        var (rangeFrom, rangeTo) = ResolveRange(interval, limit, from, to);
        var candles = await marketData.GetHistoricalCandlesAsync(symbol, interval, rangeFrom, rangeTo, ct);
        return Ok(candles);
    }

    // GET /api/marketdata/patterns?symbol=BTCUSDT&interval=1h&limit=300
    // GET /api/marketdata/patterns?symbol=BTCUSDT&interval=1h&from=2025-03-01&to=2025-03-10
    // Returns each detected pattern's name plus the timestamp range of every
    // candle involved, so the chart can draw one band spanning all of them
    // rather than a badge per candle (Dashboard-Frontend-Requirements.md item 6).
    [HttpGet("patterns")]
    public async Task<IActionResult> GetPatterns(
        [FromQuery] string symbol, [FromQuery] string interval, [FromQuery] int limit,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        CancellationToken ct)
    {
        var (rangeFrom, rangeTo) = ResolveRange(interval, limit, from, to);
        var candles = await marketData.GetHistoricalCandlesAsync(symbol, interval, rangeFrom, rangeTo, ct);
        var matches = patternDetector.Detect(candles);

        var result = matches.Select(m => new
        {
            m.Name,
            m.Description,
            StartTime = candles[m.StartIndex].OpenTime,
            EndTime = candles[m.EndIndex].OpenTime,
        });

        return Ok(result);
    }

    // GET /api/marketdata/signals?symbol=BTCUSDT&count=50 — live mode.
    // GET /api/marketdata/signals?symbol=BTCUSDT&interval=1h&from=2025-03-01&to=2025-03-10 — historical mode.
    //
    // Live mode reads recent entries from the per-symbol Redis Stream that
    // SignalGenerator.Worker's SignalWorker publishes to (see
    // Dashboard-Frontend-Requirements.md "Signal transport") — real signals, since
    // SignalGenerator.Worker now runs SmaCrossStrategy instead of a placeholder.
    //
    // Historical mode has no live stream data for past dates, so instead it
    // fetches the requested candle range and runs the same registered
    // strategies against it via HistoricalSignalRunner — computed on demand,
    // not read from Redis. This is read-only evaluation; it never places
    // orders or sends alerts, unlike SignalGenerator.Worker running the same
    // strategy live.
    [HttpGet("signals")]
    public async Task<IActionResult> GetSignals(
        [FromQuery] string symbol, [FromQuery] int count,
        [FromQuery] string? interval, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        CancellationToken ct)
    {
        if (from is not null && to is not null && !string.IsNullOrEmpty(interval))
        {
            var candles = await marketData.GetHistoricalCandlesAsync(symbol, interval, from.Value, to.Value, ct);
            var historical = HistoricalSignalRunner.Run(strategies, candles);
            return Ok(historical);
        }

        if (count <= 0) count = 50;
        var db = redis.GetDatabase();
        var streamKey = $"signals:{symbol}";

        if (!await db.KeyExistsAsync(streamKey))
            return Ok(Array.Empty<Signal>());

        var entries = await db.StreamRangeAsync(streamKey, count: count, messageOrder: StackExchange.Redis.Order.Descending);

        var signals = entries
            .Select(e => e.Values.FirstOrDefault(v => v.Name == "data"))
            .Where(v => !v.Value.IsNullOrEmpty)
            .Select(v => JsonSerializer.Deserialize<Signal>((string)v.Value!))
            .Where(s => s is not null)
            .ToList();

        return Ok(signals);
    }

    // Shared "what range of history are we looking at" logic for candles/patterns:
    // an explicit from/to (the date range picker) wins; otherwise fall back to
    // "the last `limit` candles up to now" for live/default viewing.
    private static (DateTimeOffset From, DateTimeOffset To) ResolveRange(
        string interval, int limit, DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from is not null && to is not null)
            return (from.Value, to.Value);

        if (limit <= 0) limit = 300;
        var resolvedTo = DateTimeOffset.UtcNow;
        var resolvedFrom = resolvedTo - (IntervalDuration.ToTimeSpan(interval) * limit);
        return (resolvedFrom, resolvedTo);
    }
}
