using TradingRobot.Domain.Models;

namespace TradingRobot.Domain.Abstractions;

// One implementation talks to the real exchange (Binance); a second, purely
// in-memory implementation is used by the backtester so strategy code never changes.
public interface IBroker
{
    Task<Order> PlaceOrderAsync(Order order, CancellationToken ct = default);
    Task<Order> GetOrderAsync(Guid orderId, CancellationToken ct = default);
    Task CancelOrderAsync(Guid orderId, CancellationToken ct = default);
}
