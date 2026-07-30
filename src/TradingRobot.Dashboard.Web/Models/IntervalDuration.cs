namespace TradingRobot.Dashboard.Web.Models;

// Approximate duration per timeframe string, used only to compute a "give me the
// last N candles" start time for the REST candle endpoint — not used for anything
// that needs to be exact (Binance itself is the source of truth for candle boundaries).
public static class IntervalDuration
{
    public static TimeSpan ToTimeSpan(string interval) => interval switch
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
