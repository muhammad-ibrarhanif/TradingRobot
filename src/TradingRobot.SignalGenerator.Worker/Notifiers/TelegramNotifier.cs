using System.Net.Http.Json;
using TradingRobot.Domain.Abstractions;
using TradingRobot.Domain.Models;

namespace TradingRobot.SignalGenerator.Worker.Notifiers;

// Sends a message via the Telegram Bot API (bot token + chat id from config/user-secrets).
// This is the "you look at the UI, you press the button" half of the Signal Generator.
public sealed class TelegramNotifier(HttpClient httpClient, IConfiguration config) : INotifier
{
    public async Task NotifyAsync(Signal signal, CancellationToken ct = default)
    {
        var botToken = config["Telegram:BotToken"];
        var chatId = config["Telegram:ChatId"];
        if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(chatId)) return;

        var text = $"🔔 {signal.Symbol} {signal.Side} signal\nReason: {signal.Reason}\nConfidence: {signal.Confidence:P0}";
        var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
        await httpClient.PostAsJsonAsync(url, new { chat_id = chatId, text }, ct);
    }
}
