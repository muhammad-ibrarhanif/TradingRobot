using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using TradingRobot.Dashboard.Web.Models;
using TradingRobot.Domain.Abstractions;
using TradingRobot.Domain.Models;
using TradingRobot.MarketData.BinanceNet;
using TradingRobot.PatternDetection;

namespace TradingRobot.Dashboard.Web.Controllers.Api;

[ApiController]
[Route("api/marketdata")]
public sealed class MarketDataApiController(
    IMarketDataProvider marketData,
    ISymbolCatalog symbolCatalog,
    PatternDetector patternDetector,
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
    [HttpGet("candles")]
    public async Task<IActionResult> GetCandles(
        [FromQuery] string symbol, [FromQuery] string interval, [FromQuery] int limit,
        CancellationToken ct)
    {
        if (limit <= 0) limit = 300;
        var to = DateTimeOffset.UtcNow;
        var from = to - (IntervalDuration.ToTimeSpan(interval) * limit);

        var candles = await marketData.GetHistoricalCandlesAsync(symbol, interval, from, to, ct);
        return Ok(candles);
    }

    // GET /api/marketdata/patterns?symbol=BTCUSDT&interval=1h&limit=300
    // Returns each detected pattern's name plus the timestamp range of every
    // candle involved, so the chart can draw one band spanning all of them
    // rather than a badge per candle (Dashboard-Frontend-Requirements.md item 6).
    [HttpGet("patterns")]
    public async Task<IActionResult> GetPatterns(
        [FromQuery] string symbol, [FromQuery] string interval, [FromQuery] int limit,
        CancellationToken ct)
    {
        if (limit <= 0) limit = 300;
        var to = DateTimeOffset.UtcNow;
        var from = to - (IntervalDuration.ToTimeSpan(interval) * limit);

        var candles = await marketData.GetHistoricalCandlesAsync(symbol, interval, from, to, ct);
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

    // GET /api/marketdata/signals?symbol=BTCUSDT&count=50
    // Reads recent entries from the per-symbol Redis Stream that SignalGenerator.Worker's
    // SignalWorker publishes to (see Dashboard-Frontend-Requirements.md "Signal
    // transport"). Returns an empty list until at least one non-placeholder
    // strategy is registered in SignalGenerator.Worker's Program.cs — the plumbing
    // is real, there's just nothing generating signals yet.
    [HttpGet("signals")]
    public async Task<IActionResult> GetSignals([FromQuery] string symbol, [FromQuery] int count, CancellationToken ct)
    {
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
}
