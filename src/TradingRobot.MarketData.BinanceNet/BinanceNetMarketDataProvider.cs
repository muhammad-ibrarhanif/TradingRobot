using System.Threading.Channels;
using Binance.Net.Interfaces.Clients;
using TradingRobot.Domain.Abstractions;
using TradingRobot.Domain.Models;

namespace TradingRobot.MarketData.BinanceNet;

// Replaces the hand-rolled TradingRobot.MarketData.Binance client for Dashboard.Web
// only (see Dashboard-Frontend-Requirements.md "Data layer — decision change").
// Binance.Net's REST/socket client surface has shifted across major versions —
// method/property names below match the commonly-documented 11.x shape, but
// verify against whatever version actually restores locally and adjust if the
// compiler disagrees. That's expected here since this was written without NuGet
// access to check against the real package.
public sealed class BinanceNetMarketDataProvider(
    IBinanceRestClient restClient,
    IBinanceSocketClient socketClient) : IMarketDataProvider, ISymbolCatalog
{
    public async Task<IReadOnlyList<Candle>> GetHistoricalCandlesAsync(
        string symbol, string interval, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        // Binance hard-caps every klines response at 1000 candles regardless of
        // the requested time range — a bare single call with limit:1000 silently
        // truncated any range longer than that (a full day of 1m candles is 1440),
        // which is what capped historical replay at "168/1000" instead of the
        // full day. Page through in 1000-candle chunks, advancing past the last
        // candle returned each time, until the range is exhausted.
        var all = new List<Candle>();
        var cursor = from;
        var step = IntervalSpan(interval);

        while (cursor < to)
        {
            var result = await restClient.SpotApi.ExchangeData.GetKlinesAsync(
                symbol,
                IntervalMapping.ToKlineInterval(interval),
                cursor.UtcDateTime,
                to.UtcDateTime,
                limit: 1000,
                ct: ct);

            if (!result.Success)
                throw new InvalidOperationException($"Binance.Net GetKlinesAsync failed: {result.Error}");

            var page = result.Data.ToList();
            if (page.Count == 0) break;

            all.AddRange(page.Select(k => new Candle(
                Symbol: symbol,
                Interval: interval,
                OpenTime: new DateTimeOffset(k.OpenTime, TimeSpan.Zero),
                Open: k.OpenPrice,
                High: k.HighPrice,
                Low: k.LowPrice,
                Close: k.ClosePrice,
                Volume: k.Volume
            )));

            cursor = new DateTimeOffset(page[^1].OpenTime, TimeSpan.Zero) + step;

            if (page.Count < 1000) break; // short page = nothing left in range
        }

        return all;
    }

    // Local interval-string-to-TimeSpan mapping, same small duplicated helper
    // pattern used elsewhere (e.g. SignalWorker.IntervalSpan) rather than a
    // shared dependency for something this small.
    private static TimeSpan IntervalSpan(string interval) => interval switch
    {
        "1m" => TimeSpan.FromMinutes(1),
        "3m" => TimeSpan.FromMinutes(3),
        "5m" => TimeSpan.FromMinutes(5),
        "15m" => TimeSpan.FromMinutes(15),
        "30m" => TimeSpan.FromMinutes(30),
        "1h" => TimeSpan.FromHours(1),
        "4h" => TimeSpan.FromHours(4),
        "1d" => TimeSpan.FromDays(1),
        "1w" => TimeSpan.FromDays(7),
        _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, "Unsupported interval.")
    };

    public async IAsyncEnumerable<Candle> StreamCandlesAsync(
        string symbol, string interval,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = Channel.CreateUnbounded<Candle>();

        var subscription = await socketClient.SpotApi.ExchangeData.SubscribeToKlineUpdatesAsync(
            symbol,
            IntervalMapping.ToKlineInterval(interval),
            update =>
            {
                var k = update.Data.Data;

                // Only emit closed candles — same "react on close, not on every tick"
                // rule the hand-rolled client followed (Binance's own `x`/"final" flag).
                if (!k.Final) return;

                channel.Writer.TryWrite(new Candle(
                    Symbol: symbol,
                    Interval: interval,
                    OpenTime: new DateTimeOffset(k.OpenTime, TimeSpan.Zero),
                    Open: k.OpenPrice,
                    High: k.HighPrice,
                    Low: k.LowPrice,
                    Close: k.ClosePrice,
                    Volume: k.Volume));
            },
            ct: ct);

        if (!subscription.Success)
            throw new InvalidOperationException($"Binance.Net SubscribeToKlineUpdatesAsync failed: {subscription.Error}");

        await using var registration = ct.Register(() =>
        {
            channel.Writer.TryComplete();
            _ = socketClient.UnsubscribeAsync(subscription.Data);
        });

        await foreach (var candle in channel.Reader.ReadAllAsync(ct))
            yield return candle;
    }

    public async Task<IReadOnlyList<string>> GetAvailableSymbolsAsync(CancellationToken ct = default)
    {
        var result = await restClient.SpotApi.ExchangeData.GetExchangeInfoAsync(ct: ct);
        if (!result.Success)
            throw new InvalidOperationException($"Binance.Net GetExchangeInfoAsync failed: {result.Error}");

        return result.Data.Symbols
            .Where(s => s.Status == Binance.Net.Enums.SymbolStatus.Trading)
            .Select(s => s.Name)
            .OrderBy(n => n)
            .ToList();
    }
}
