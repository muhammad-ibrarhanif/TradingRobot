using TradingRobot.Domain.Abstractions;
using TradingRobot.Domain.Models;

namespace TradingRobot.SignalGenerator.Worker.Notifiers;

// Stub — plug in SendGrid/SES/SMTP here. Kept separate from TelegramNotifier so
// either channel (or both) can be enabled per deployment via DI registration.
public sealed class EmailNotifier(ILogger<EmailNotifier> logger) : INotifier
{
    public Task NotifyAsync(Signal signal, CancellationToken ct = default)
    {
        logger.LogInformation("EMAIL (stub): {Symbol} {Side} — {Reason}", signal.Symbol, signal.Side, signal.Reason);
        return Task.CompletedTask;
    }
}
