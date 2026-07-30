using Binance.Net.Enums;

namespace TradingRobot.MarketData.BinanceNet;

// Our Candle.Interval is a plain string ("1m", "5m", "1h", "1d", ...) so every
// service (including the hand-rolled TradingRobot.MarketData.Binance client) agrees
// on the same shape. Binance.Net wants its own KlineInterval enum, so this is the
// one place that translates between the two.
public static class IntervalMapping
{
    public static KlineInterval ToKlineInterval(string interval) => interval switch
    {
        "1m" => KlineInterval.OneMinute,
        "3m" => KlineInterval.ThreeMinutes,
        "5m" => KlineInterval.FiveMinutes,
        "15m" => KlineInterval.FifteenMinutes,
        "30m" => KlineInterval.ThirtyMinutes,
        "1h" => KlineInterval.OneHour,
        "4h" => KlineInterval.FourHour,
        "1d" => KlineInterval.OneDay,
        "1w" => KlineInterval.OneWeek,
        _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, "Unsupported interval — add it here first.")
    };
}
