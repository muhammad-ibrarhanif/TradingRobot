using TradingRobot.Domain.Models;

namespace TradingRobot.Domain.Abstractions;

public interface IMarketDataProvider
{
    // Historical candles for backtesting.
    Task<IReadOnlyList<Candle>> GetHistoricalCandlesAsync(
        string symbol, string interval, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);

    // Live candle stream for the Signal Generator / Live Execution Bot.
    IAsyncEnumerable<Candle> StreamCandlesAsync(
        string symbol, string interval, CancellationToken ct = default);
}
