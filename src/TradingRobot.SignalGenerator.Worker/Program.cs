using TradingRobot.Domain.Abstractions;
using TradingRobot.MarketData.Binance;
using TradingRobot.SignalGenerator.Worker;
using TradingRobot.SignalGenerator.Worker.Notifiers;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

builder.Services.Configure<BinanceOptions>(builder.Configuration.GetSection("Binance"));
builder.Services.AddSingleton<BinanceWebSocketClient>();
builder.Services.AddHttpClient<BinanceRestClient>(); // used once at startup to preload history — see SignalWorker
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
//
// Three independent signal sources per Dashboard-Frontend-Requirements.md "Signal
// generation — patterns vs indicators vs combined": price action alone
// (PatternBasedStrategy), an indicator alone (SmaCrossStrategy), and both agreeing
// together (ConfirmedStrategy). All three run at once; the dashboard already
// colors/labels signals per StrategyName so you can tell which source produced
// which marker. Chart highlighting for patterns is a separate, always-on layer
// (MarketDataApiController.GetPatterns) — it doesn't depend on which of these are
// registered.
builder.Services.AddSingleton<IStrategy>(new TradingRobot.Strategies.PatternBasedStrategy());
builder.Services.AddSingleton<IStrategy>(new TradingRobot.Strategies.SmaCrossStrategy());
builder.Services.AddSingleton<IStrategy>(new TradingRobot.Strategies.ConfirmedStrategy());

builder.Services.AddHostedService<SignalWorker>();

var host = builder.Build();
host.Run();
