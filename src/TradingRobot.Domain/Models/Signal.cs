namespace TradingRobot.Domain.Models;

// Emitted by a strategy when it thinks action is warranted. The Signal Generator
// turns this into a Telegram/email alert; the Live Execution Bot turns it into an Order.
// StrategyName identifies which of the (potentially many) concurrently running
// strategies produced this signal — required once more than one strategy can be
// evaluating the same candle stream at once, so downstream consumers (chart
// markers, alerts, order placement) can tell them apart.
public sealed record Signal(
    string StrategyName,
    string Symbol,
    OrderSide Side,
    string Reason,
    decimal Confidence,
    DateTimeOffset GeneratedAt);
