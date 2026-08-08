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
// Three signal sources exist per Dashboard-Frontend-Requirements.md "Signal
// generation — patterns vs indicators vs combined": price action alone
// (PatternBasedStrategy), an indicator alone (SmaCrossStrategy), and both
// agreeing together (ConfirmedStrategy). Indicators are explicitly paused for now
// — decision was to get price action right first, indicators come back later —
// so only PatternBasedStrategy is registered below. SmaCrossStrategy/
// ConfirmedStrategy stay in the codebase, just commented out here, so turning
// them back on later is a one-line change, not rebuilding anything.
builder.Services.AddSingleton<IStrategy>(new TradingRobot.Strategies.PatternBasedStrategy());
// builder.Services.AddSingleton<IStrategy>(new TradingRobot.Strategies.SmaCrossStrategy());
// builder.Services.AddSingleton<IStrategy>(new TradingRobot.Strategies.ConfirmedStrategy());

builder.Services.AddHostedService<SignalWorker>();

var host = builder.Build();
host.Run();
