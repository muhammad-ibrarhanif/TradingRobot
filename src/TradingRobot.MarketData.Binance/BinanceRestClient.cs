using System.Text.Json;
using Microsoft.Extensions.Options;
using TradingRobot.Domain.Models;

namespace TradingRobot.MarketData.Binance;

// Historical OHLCV via Binance's public /api/v3/klines endpoint.
// No API key required for market data — only order placement needs signed requests.
public sealed class BinanceRestClient(HttpClient httpClient, IOptions<BinanceOptions> options)
{
    private readonly BinanceOptions _options = options.Value;

    public async Task<IReadOnlyList<Candle>> GetKlinesAsync(
        string symbol, string interval, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        var url = $"{_options.RestBaseUrl}/api/v3/klines" +
                   $"?symbol={symbol.ToUpperInvariant()}&interval={interval}" +
                   $"&startTime={from.ToUnixTimeMilliseconds()}&endTime={to.ToUnixTimeMilliseconds()}&limit=1000";

        using var response = await httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var raw = await JsonSerializer.DeserializeAsync<List<JsonElement>>(stream, cancellationToken: ct)
                  ?? [];

        // Binance kline array shape: [openTime, open, high, low, close, volume, closeTime, ...]
        return raw.Select(k => new Candle(
            Symbol: symbol,
            Interval: interval,
            OpenTime: DateTimeOffset.FromUnixTimeMilliseconds(k[0].GetInt64()),
            Open: decimal.Parse(k[1].GetString()!),
            High: decimal.Parse(k[2].GetString()!),
            Low: decimal.Parse(k[3].GetString()!),
            Close: decimal.Parse(k[4].GetString()!),
            Volume: decimal.Parse(k[5].GetString()!)
        )).ToList();
    }
}
