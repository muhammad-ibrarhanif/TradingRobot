using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TradingRobot.Domain.Models;

namespace TradingRobot.MarketData.Binance;

// Live kline stream via Binance's combined WebSocket ("<symbol>@kline_<interval>").
// Used by the Signal Generator and Live Execution Bot — never by the Strategy Tester.
public sealed class BinanceWebSocketClient(IOptions<BinanceOptions> options)
{
    private readonly BinanceOptions _options = options.Value;

    public async IAsyncEnumerable<Candle> StreamKlinesAsync(
        string symbol, string interval, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var streamName = $"{symbol.ToLowerInvariant()}@kline_{interval}";
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"{_options.WebSocketBaseUrl}/{streamName}"), ct);

        var buffer = new byte[16 * 1024];
        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) yield break;

            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
            var k = doc.RootElement.GetProperty("k");

            // Binance sends every in-progress tick for the current candle; "x" is true
            // only once the candle has actually closed — that's what strategies should react to.
            if (!k.GetProperty("x").GetBoolean()) continue;

            yield return new Candle(
                Symbol: symbol,
                Interval: interval,
                OpenTime: DateTimeOffset.FromUnixTimeMilliseconds(k.GetProperty("t").GetInt64()),
                Open: decimal.Parse(k.GetProperty("o").GetString()!),
                High: decimal.Parse(k.GetProperty("h").GetString()!),
                Low: decimal.Parse(k.GetProperty("l").GetString()!),
                Close: decimal.Parse(k.GetProperty("c").GetString()!),
                Volume: decimal.Parse(k.GetProperty("v").GetString()!));
        }
    }
}
