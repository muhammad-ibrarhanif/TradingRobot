namespace TradingRobot.Domain.Models;

// One OHLCV bar. Shared by the backtester, the live feed, and the chart UI
// so all four apps agree on what "a bar of data" means.
public sealed record Candle(
    string Symbol,
    string Interval,       // e.g. "1m", "5m", "1h", "1d"
    DateTimeOffset OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume);
