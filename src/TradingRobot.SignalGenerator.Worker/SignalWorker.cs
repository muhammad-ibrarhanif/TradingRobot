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
    IEnumerable<INotifier> notifiers,
    IEnumerable<IStrategy> strategies,
    ILogger<SignalWorker> logger,
    IConfiguration config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var symbol = config["Watch:Symbol"] ?? "BTCUSDT";
        var interval = config["Watch:Interval"] ?? "5m";
        var history = new List<Candle>();

        await foreach (var candle in marketData.StreamKlinesAsync(symbol, interval, stoppingToken))
        {
            history.Add(candle);

            foreach (var strategy in strategies)
            {
                var signal = strategy.OnCandle(candle, history);
                if (signal is null) continue;

                logger.LogInformation("[{Strategy}] Signal: {Symbol} {Side} — {Reason}",
                    signal.StrategyName, signal.Symbol, signal.Side, signal.Reason);

                // TODO: also publish `signal` to a Redis Stream here (see
                // Dashboard-Frontend-Requirements.md) so Dashboard.Web can render
                // buy/sell markers without re-running strategies itself.
                foreach (var notifier in notifiers)
                    await notifier.NotifyAsync(signal, stoppingToken);
            }
        }
    }
}
