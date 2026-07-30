// TradingRobot.AppHost — the Aspire orchestrator.
// Running this project (F5 / `dotnet run` / `aspire run`) spins up Redis in Docker
// and every service below, wires connection strings/env vars between them,
// and opens the Aspire dashboard (traces, logs, metrics) for the whole system.
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// Shared cache: candles, order books, computed indicators, dedup keys for alerts.
var redis = builder.AddRedis("redis")
    .WithRedisCommander() // web UI to inspect cache contents in dev, http://localhost:8081
    .WithLifetime(ContainerLifetime.Persistent);

// Optional: persistent store for historical OHLCV + backtest results.
// Swap for a real Postgres/TimescaleDB instance when ready; container is dev-only.
var postgres = builder.AddPostgres("postgres")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPgAdmin();
var tradingDb = postgres.AddDatabase("tradingdb");

// 1. Strategy Tester — backtest engine + chart API. No live trading.
var strategyTester = builder.AddProject<Projects.TradingRobot_StrategyTester_Api>("strategy-tester")
    .WithReference(redis)
    .WithReference(tradingDb)
    .WithExternalHttpEndpoints();

// 2. Signal Generator — watches market data, pushes Telegram/email alerts to a human.
var signalGenerator = builder.AddProject<Projects.TradingRobot_SignalGenerator_Worker>("signal-generator")
    .WithReference(redis)
    .WaitFor(redis);

// 3. Live Execution Bot — automated broker execution driven by real-time data.
var liveExecutionBot = builder.AddProject<Projects.TradingRobot_LiveExecutionBot_Worker>("live-execution-bot")
    .WithReference(redis)
    .WithReference(tradingDb)
    .WaitFor(redis);

// 4. Dashboard — shared web UI: strategy tester charts + live execution bot status.
// This is also the seed for the eventual full TradingView-clone frontend.
builder.AddProject<Projects.TradingRobot_Dashboard_Web>("dashboard")
    .WithReference(strategyTester)
    .WithReference(redis)
    .WithExternalHttpEndpoints();

builder.Build().Run();



//What's Ready to Build Next
//Based on the architecture doc's recommended path:
//1.	Redis candle caching in BacktestEndpoints — cache GetKlinesAsync results to avoid hammering Binance between repeated test runs
//2.	Signal dedup in SignalWorker — use Redis SET NX with a TTL keyed on (symbol, side, candle.OpenTime) to avoid duplicate Telegram alerts on restart
//3.	BinanceBroker signing — HMAC-SHA256 on testnet before any real money
//4.	Dashboard — proxy the backtest API + live-execution-bot order status into a unified UI
//5.	Additional strategies — RSI, Bollinger Bands, etc. as IStrategy implementations in StrategyTester.Api/Strategies/
//6.	Strategy selector in the backtest UI — dropdown to pick which IStrategy to run instead of hardcoded SmaCrossStrategy