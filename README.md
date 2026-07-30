# Trading Robot — dev environment

Requires: .NET 9 SDK, Docker Desktop (or another Docker daemon) running, and the
Aspire workload:

```
dotnet workload install aspire
```

## Run everything

```
cd src/TradingRobot.AppHost
dotnet run
```

This starts, via Docker, on first run:
- **Redis** (shared cache: candles, dedup keys for alerts, live-execution status) + RedisCommander UI
- **Postgres** (historical candles / backtest results) + pgAdmin

...and then starts all four services in-process, wiring connection strings between
them automatically. The Aspire dashboard opens in your browser (URL printed in the
console) showing live logs, traces, and metrics for every service — this is the
single biggest quality-of-life win over running five separate `dotnet run` terminals
by hand, which is why the whole solution is structured as an Aspire app rather than
a loose collection of projects.

## Services

| Project | Role |
|---|---|
| `TradingRobot.AppHost` | Orchestrator — Docker/Redis/Postgres + service wiring. Run this. |
| `TradingRobot.StrategyTester.Api` | Backtest engine + candlestick chart UI (`wwwroot`). No live trading. |
| `TradingRobot.SignalGenerator.Worker` | Watches live Binance data, sends Telegram/email alerts. |
| `TradingRobot.LiveExecutionBot.Worker` | Watches live data, places real orders via `IBroker`. Ships with a stub broker that rejects everything — see `Brokers/BinanceBroker.cs` for what's left before this can place a real order. |
| `TradingRobot.Dashboard.Web` | Web UI shell — will grow into the TradingView-clone frontend. |
| `TradingRobot.Domain` | Shared models (`Candle`, `Order`, `Signal`) and interfaces (`IStrategy`, `IBroker`, `IMarketDataProvider`, `INotifier`) used by every service above. |
| `TradingRobot.MarketData.Binance` | Binance REST (historical klines) + WebSocket (live klines) client. |

## Why one `IStrategy` interface

A strategy is written once against `IStrategy.OnCandle(candle, history)` and runs
unmodified in three places: fed historical data in the Strategy Tester, fed live
data in the Signal Generator (signal -> Telegram/email), and fed live data in the
Live Execution Bot (signal -> real order). This is the main thing worth protecting
as the codebase grows — resist the urge to special-case strategy logic per service.

## Before going live with real money

1. `BinanceBroker.PlaceOrderAsync` is a stub that always rejects. Implement HMAC
   request signing and point at `testnet.binance.vision` first.
2. Backtest the strategy in the Strategy Tester across multiple market regimes.
3. Run it through the Signal Generator for a while with a human confirming calls
   before letting the Live Execution Bot place real orders.
4. Add position sizing / risk limits — the current `ExecutionWorker` uses a fixed
   quantity from config, which is not a real risk management strategy.

## Secrets

Binance API keys and Telegram bot tokens belong in user-secrets locally
(`dotnet user-secrets set "Binance:ApiKey" "..."` from each worker project) and in
a real secrets store (Key Vault, etc.) in any deployed environment. Never commit
them to `appsettings.json`.
