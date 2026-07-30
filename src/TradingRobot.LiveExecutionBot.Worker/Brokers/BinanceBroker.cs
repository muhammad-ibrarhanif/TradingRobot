using TradingRobot.Domain.Abstractions;
using TradingRobot.Domain.Models;

namespace TradingRobot.LiveExecutionBot.Worker.Brokers;

// TODO before going live: HMAC-SHA256 request signing with BinanceOptions.ApiSecret,
// timestamp/recvWindow handling, and error mapping from Binance's error codes to
// OrderStatus.Rejected. Point BinanceOptions.UseTestnet at testnet.binance.vision
// until this has been exercised against real (test) fills.
public sealed class BinanceBroker(HttpClient httpClient, ILogger<BinanceBroker> logger) : IBroker
{
    public Task<Order> PlaceOrderAsync(Order order, CancellationToken ct = default)
    {
        logger.LogWarning("BinanceBroker.PlaceOrderAsync is a stub — no order was actually sent. {@Order}", order);
        order.Status = OrderStatus.Rejected;
        return Task.FromResult(order);
    }

    public Task<Order> GetOrderAsync(Guid orderId, CancellationToken ct = default) =>
        throw new NotImplementedException("Wire up GET /api/v3/order with a signed request.");

    public Task CancelOrderAsync(Guid orderId, CancellationToken ct = default) =>
        throw new NotImplementedException("Wire up DELETE /api/v3/order with a signed request.");
}
