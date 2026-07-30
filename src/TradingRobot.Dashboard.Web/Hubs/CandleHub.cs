using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using TradingRobot.Domain.Abstractions;

namespace TradingRobot.Dashboard.Web.Hubs;

// Client calls SubscribeToSymbol(symbol, interval) after connecting (and again on
// every symbol/timeframe switch); the hub streams closed candles back to that
// same caller only, via the "CandleUpdate" client method, until the connection
// resubscribes or disconnects. One IMarketDataProvider.StreamCandlesAsync loop
// per connection — fine for the handful of concurrent viewers this is built for,
// revisit (e.g. share one upstream subscription per symbol across connections)
// if that stops being true.
public sealed class CandleHub(IMarketDataProvider marketData, ILogger<CandleHub> logger) : Hub
{
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> ActiveStreams = new();

    public Task SubscribeToSymbol(string symbol, string interval)
    {
        CancelExistingStream(Context.ConnectionId);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(Context.ConnectionAborted);
        ActiveStreams[Context.ConnectionId] = cts;

        _ = StreamToCallerAsync(Clients.Caller, symbol, interval, cts.Token);
        return Task.CompletedTask;
    }

    private async Task StreamToCallerAsync(ISingleClientProxy caller, string symbol, string interval, CancellationToken ct)
    {
        try
        {
            await foreach (var candle in marketData.StreamCandlesAsync(symbol, interval, ct))
                await caller.SendAsync("CandleUpdate", candle, ct);
        }
        catch (OperationCanceledException)
        {
            // Expected on resubscribe/disconnect.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Candle stream for {Symbol}/{Interval} failed", symbol, interval);
        }
    }

    private static void CancelExistingStream(string connectionId)
    {
        if (ActiveStreams.TryRemove(connectionId, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        CancelExistingStream(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
