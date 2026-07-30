namespace TradingRobot.Domain.Models;

// Represents an order both in backtests (simulated fills) and live execution
// (real broker fills). Same shape everywhere keeps the strategy code identical
// between the Strategy Tester and the Live Execution Bot.
public sealed class Order
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Symbol { get; init; }
    public required OrderSide Side { get; init; }
    public required OrderType Type { get; init; }
    public required decimal Quantity { get; init; }
    public decimal? LimitPrice { get; init; }
    public decimal? StopPrice { get; init; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal? FilledPrice { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FilledAt { get; set; }
}
