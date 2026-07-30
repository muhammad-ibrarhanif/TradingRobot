using TradingRobot.Domain.Models;

namespace TradingRobot.Domain.Abstractions;

// Implemented by TelegramNotifier and EmailNotifier in the Signal Generator worker.
public interface INotifier
{
    Task NotifyAsync(Signal signal, CancellationToken ct = default);
}
