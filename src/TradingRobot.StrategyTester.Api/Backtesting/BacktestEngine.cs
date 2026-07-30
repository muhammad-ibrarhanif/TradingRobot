using TradingRobot.Domain.Abstractions;
using TradingRobot.Domain.Models;

namespace TradingRobot.StrategyTester.Api.Backtesting;

// Deliberately simple, single-position, long/flat simulation — enough to validate
// a strategy's entry/exit logic and see it plotted before adding realism
// (fees, slippage, partial fills, shorting, position sizing).
public sealed class BacktestEngine
{
    public BacktestResult Run(IStrategy strategy, IReadOnlyList<Candle> candles, decimal startingEquity = 10_000m)
    {
        var trades = new List<TradeMarker>();
        var history = new List<Candle>();
        decimal equity = startingEquity;
        decimal? entryPrice = null;
        int wins = 0, losses = 0;

        foreach (var candle in candles)
        {
            history.Add(candle);
            var signal = strategy.OnCandle(candle, history);
            if (signal is null) continue;

            if (signal.Side == OrderSide.Buy && entryPrice is null)
            {
                entryPrice = candle.Close;
                trades.Add(new TradeMarker(candle.OpenTime, candle.Close, OrderSide.Buy, signal.Reason));
            }
            else if (signal.Side == OrderSide.Sell && entryPrice is not null)
            {
                var pnl = candle.Close - entryPrice.Value;
                equity += pnl / entryPrice.Value * equity;
                if (pnl >= 0) wins++; else losses++;
                trades.Add(new TradeMarker(candle.OpenTime, candle.Close, OrderSide.Sell, signal.Reason));
                entryPrice = null;
            }
        }

        return new BacktestResult(strategy.Name, candles.FirstOrDefault()?.Symbol ?? "",
            candles, trades, startingEquity, equity, wins, losses);
    }
}
