namespace TradingRobot.Domain.Models;

public enum OrderSide { Buy, Sell }

public enum OrderType { Market, Limit, StopMarket, StopLimit }

public enum OrderStatus { Pending, Submitted, PartiallyFilled, Filled, Cancelled, Rejected }
