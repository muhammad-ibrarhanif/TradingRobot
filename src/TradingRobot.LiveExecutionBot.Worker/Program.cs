using TradingRobot.Domain.Abstractions;
using TradingRobot.LiveExecutionBot.Worker;
using TradingRobot.LiveExecutionBot.Worker.Brokers;
using TradingRobot.MarketData.Binance;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

builder.Services.Configure<BinanceOptions>(builder.Configuration.GetSection("Binance"));
builder.Services.AddSingleton<BinanceWebSocketClient>();
builder.AddRedisClient(connectionName: "redis"); // caches last-known order/position state

builder.Services.AddHttpClient<BinanceBroker>();
builder.Services.AddSingleton<IBroker>(sp => sp.GetRequiredService<BinanceBroker>());

// Same strategy contract as the Signal Generator / Strategy Tester, and the same
// IEnumerable<IStrategy> multi-strategy plumbing — but see the RISK NOTE in
// ExecutionWorker.cs before registering more than one strategy here. Only register
// a strategy once it's been validated by backtest and a supervised run through the
// Signal Generator.
builder.Services.AddSingleton<IStrategy>(new TradingRobot.LiveExecutionBot.Worker.Strategies.PlaceholderStrategy());

builder.Services.AddHostedService<ExecutionWorker>();

var host = builder.Build();
host.Run();
