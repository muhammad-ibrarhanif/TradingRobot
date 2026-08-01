using System.Text.Json;
using StackExchange.Redis;
using TradingRobot.Domain.Abstractions;
using TradingRobot.Domain.Models;
using TradingRobot.MarketData.Binance;

namespace TradingRobot.SignalGenerator.Worker;

// Watches the live candle stream and fans out any Signal to every registered
// INotifier (Telegram + email today, more channels later). No orders are ever placed here.
//
// Every registered IStrategy runs independently against the same candle stream —
// no strategy knows about, or is affected by, any other. Add/remove strategies by
// changing the DI registrations in Program.cs; this worker doesn't change.
public sealed class SignalWorker(
    BinanceWebSocketClient marketData,
    BinanceRestClient historicalMarketData,
    IEnumerable<INotifier> notifiers,
    IEnumerable<IStrategy> strategies,
    IConnectionMultiplexer redis,
    ILogger<SignalWorker> logger,
    IConfiguration config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var symbol = config["Watch:Symbol"] ?? "BTCUSDT";
        var interval = config["Watch:Interval"] ?? "5m";
        var db = redis.GetDatabase();

        // Preload recent history via REST before starting the live stream.
        // Without this, `history` starts empty and SmaCrossStrategy (needs
        // slowPeriod+1 = 31 closed candles) couldn't evaluate a single crossover
        // until 31 live candles had actually closed — ~2.5 hours of continuous
        // runtime at the default 5m interval, after every restart. 60 candles
        // gives every registered strategy a reasonable buffer to start from.
        var seedFrom = DateTimeOffset.UtcNow - (IntervalSpan(interval) * 60);
        var history = (await historicalMarketData.GetKlinesAsync(symbol, interval, seedFrom, DateTimeOffset.UtcNow, stoppingToken))
            .ToList();
        logger.LogInformation("Preloaded {Count} historical candles for {Symbol}/{Interval} before going live", history.Count, symbol, interval);

        await foreach (var candle in marketData.StreamKlinesAsync(symbol, interval, stoppingToken))
        {
            history.Add(candle);

            foreach (var strategy in strategies)
            {
                var signal = strategy.OnCandle(candle, history);
                if (signal is null) continue;

                logger.LogInformation("[{Strategy}] Signal: {Symbol} {Side} — {Reason}",
                    signal.StrategyName, signal.Symbol, signal.Side, signal.Reason);

                // Wire format locked in Dashboard-Frontend-Requirements.md ("Signal
                // transport"): one Redis Stream per symbol, single "data" field
                // holding the JSON-serialized Signal. Dashboard.Web's
                // MarketDataApiController.GetSignals reads this same stream/field.
                await db.StreamAddAsync($"signals:{signal.Symbol}", "data", JsonSerializer.Serialize(signal));

                foreach (var notifier in notifiers)
                    await notifier.NotifyAsync(signal, stoppingToken);
            }
        }
    }

    // Same interval-string-to-duration mapping used by Dashboard.Web's
    // IntervalDuration — duplicated locally rather than shared across services
    // for such a small helper; worth consolidating into TradingRobot.Domain if a
    // third place ends up needing it.
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
}
