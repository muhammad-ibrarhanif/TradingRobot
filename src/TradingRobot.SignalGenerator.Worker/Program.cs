using TradingRobot.Domain.Abstractions;
using TradingRobot.MarketData.Binance;
using TradingRobot.SignalGenerator.Worker;
using TradingRobot.SignalGenerator.Worker.Notifiers;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

builder.Services.Configure<BinanceOptions>(builder.Configuration.GetSection("Binance"));
builder.Services.AddSingleton<BinanceWebSocketClient>();
builder.AddRedisClient(connectionName: "redis"); // dedup so the same signal isn't alerted twice

// AddHttpClient<T> registers TelegramNotifier as a "typed client" — it can only be
// constructed via IHttpClientFactory, so INotifier must resolve it through the
// container rather than via a second AddSingleton<INotifier, TelegramNotifier>()
// (that would try to activate it with a plain constructor and fail: no raw
// HttpClient service is registered outside the typed-client mechanism).
builder.Services.AddHttpClient<TelegramNotifier>();
builder.Services.AddSingleton<INotifier>(sp => sp.GetRequiredService<TelegramNotifier>());
builder.Services.AddSingleton<INotifier, EmailNotifier>();

// Every AddSingleton<IStrategy>(...) call below registers one more strategy to run
// concurrently against the same candle stream — SignalWorker resolves all of them
// via IEnumerable<IStrategy> and evaluates each independently, so adding a new
// strategy is just adding another line here, not touching the worker.
// No real strategy is defined yet — this is a single inert placeholder until one is.
builder.Services.AddSingleton<IStrategy>(new TradingRobot.SignalGenerator.Worker.Strategies.PlaceholderStrategy());

builder.Services.AddHostedService<SignalWorker>();

var host = builder.Build();
host.Run();
