using System.Globalization;
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
        [FromQuery] string? from, [FromQuery] string? to,
        CancellationToken ct)
    {
        var (rangeFrom, rangeTo) = ResolveRange(interval, limit, ParseUtcDate(from), ParseUtcDate(to));
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
        [FromQuery] string? from, [FromQuery] string? to,
        CancellationToken ct)
    {
        var (rangeFrom, rangeTo) = ResolveRange(interval, limit, ParseUtcDate(from), ParseUtcDate(to));
        var candles = await marketData.GetHistoricalCandlesAsync(symbol, interval, rangeFrom, rangeTo, ct);
        var matches = patternDetector.Detect(candles);

        // High/Low across every candle the pattern spans, added so the chart can
        // draw a box tightly around the pattern's actual price range instead of a
        // full-height translucent column — the full-height version technically
        // highlighted every pattern already, but read as a faint background smear
        // rather than "this candle is highlighted," which is why it kept going
        // unnoticed. See dashboard.js drawPatterns.
        var result = matches.Select(m =>
        {
            var span = candles.Skip(m.StartIndex).Take(m.EndIndex - m.StartIndex + 1).ToList();
            return new
            {
                m.Name,
                m.Description,
                StartTime = candles[m.StartIndex].OpenTime,
                EndTime = candles[m.EndIndex].OpenTime,
                High = span.Max(c => c.High),
                Low = span.Min(c => c.Low),
            };
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
        [FromQuery] string? interval, [FromQuery] string? from, [FromQuery] string? to,
        CancellationToken ct)
    {
        var (parsedFrom, parsedTo) = (ParseUtcDate(from), ParseUtcDate(to));
        if (parsedFrom is not null && parsedTo is not null && !string.IsNullOrEmpty(interval))
        {
            var candles = await marketData.GetHistoricalCandlesAsync(symbol, interval, parsedFrom.Value, InclusiveEndOfDay(parsedTo.Value), ct);
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
            return (from.Value, InclusiveEndOfDay(to.Value));

        if (limit <= 0) limit = 300;
        var resolvedTo = DateTimeOffset.UtcNow;
        var resolvedFrom = resolvedTo - (IntervalDuration.ToTimeSpan(interval) * limit);
        return (resolvedFrom, resolvedTo);
    }

    // The date-range picker sends plain `YYYY-MM-DD` (via <input type="date">),
    // which parses to midnight at the *start* of that day. Taken literally as an
    // upper bound, "01-07-2026 to 01-07-2026" is a zero-width range — the API can
    // only return whatever single candle sits at that exact instant, which is
    // exactly the "only 1 candle" bug reported after adding the replay feature.
    // A date-only `to` should mean "through the end of that calendar day," so we
    // push it to the start of the next day (exclusive upper bound) before querying.
    private static DateTimeOffset InclusiveEndOfDay(DateTimeOffset to) => to.AddDays(1);

    // `from`/`to` used to be bound as `DateTimeOffset?` directly, which meant
    // ASP.NET Core parsed a bare "2026-07-01" using the *server's local offset*
    // (DateTimeStyles.None with no explicit offset assumes local time). That
    // silently shifted which calendar day "01-07-2026" actually meant by however
    // many hours the server is offset from UTC — e.g. on a UTC+1 server, "day 1"
    // started at 23:00 UTC on the *previous* day and ran only partway into day 1,
    // not midnight-to-midnight UTC as the picker implies. Binance's own candle
    // timestamps are UTC, so parsing explicitly as UTC keeps the two consistent
    // regardless of what timezone Dashboard.Web happens to be hosted in.
    private static DateTimeOffset? ParseUtcDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var dt = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        return new DateTimeOffset(dt, TimeSpan.Zero);
    }
}
