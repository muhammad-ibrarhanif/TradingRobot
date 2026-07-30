using TradingRobot.Domain.Models;

namespace TradingRobot.StrategyTester.Api.Backtesting;

public sealed record TradeMarker(DateTimeOffset Time, decimal Price, OrderSide Side, string Reason);

public sealed record BacktestResult(
    string StrategyName,
    string Symbol,
    IReadOnlyList<Candle> Candles,
    IReadOnlyList<TradeMarker> Trades,
    decimal StartingEquity,
    decimal EndingEquity,
    int WinCount,
    int LossCount)
{
    public decimal ReturnPct => StartingEquity == 0 ? 0 : (EndingEquity - StartingEquity) / StartingEquity * 100m;
    public int TotalTrades => WinCount + LossCount;
    public decimal WinRatePct => TotalTrades == 0 ? 0 : (decimal)WinCount / TotalTrades * 100m;
}
