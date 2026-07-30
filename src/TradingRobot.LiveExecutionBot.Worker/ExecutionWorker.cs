using TradingRobot.Domain.Abstractions;
using TradingRobot.Domain.Models;
using TradingRobot.MarketData.Binance;

namespace TradingRobot.LiveExecutionBot.Worker;

// Same shape as SignalWorker, but Signals become real Orders through IBroker
// instead of alerts through INotifier. Keep this worker paused/disabled until
// at least one strategy has a proven backtest and, ideally, a period running
// through the Signal Generator with a human confirming its calls.
//
// RISK NOTE — multiple concurrent strategies here is NOT the same low-risk fan-out
// as in SignalWorker. Each strategy below independently places its own order with
// its own fixed `quantity` and no awareness of what the others are doing: run three
// strategies and you can end up with three times the position size, or strategies
// fighting each other (one buys while another sells the same symbol), with nothing
// in this worker coordinating capital allocation or net exposure across them. Do not
// enable more than one strategy here until there's a real portfolio/allocation layer
// deciding position size per strategy — that doesn't exist yet.
public sealed class ExecutionWorker(
    BinanceWebSocketClient marketData,
    IBroker broker,
    IEnumerable<IStrategy> strategies,
    ILogger<ExecutionWorker> logger,
    IConfiguration config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var symbol = config["Watch:Symbol"] ?? "BTCUSDT";
        var interval = config["Watch:Interval"] ?? "5m";
        var quantity = decimal.Parse(config["Execution:Quantity"] ?? "0.001");
        var history = new List<Candle>();

        await foreach (var candle in marketData.StreamKlinesAsync(symbol, interval, stoppingToken))
        {
            history.Add(candle);

            foreach (var strategy in strategies)
            {
                var signal = strategy.OnCandle(candle, history);
                if (signal is null) continue;

                var order = new Order { Symbol = signal.Symbol, Side = signal.Side, Type = OrderType.Market, Quantity = quantity };
                var placed = await broker.PlaceOrderAsync(order, stoppingToken);
                logger.LogInformation("[{Strategy}] Order {Id} for {Symbol} {Side} -> {Status}",
                    signal.StrategyName, placed.Id, placed.Symbol, placed.Side, placed.Status);
            }
        }
    }
}
