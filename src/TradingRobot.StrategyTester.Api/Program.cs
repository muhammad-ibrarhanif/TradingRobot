using TradingRobot.Domain.Abstractions;
using TradingRobot.MarketData.Binance;
using TradingRobot.StrategyTester.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.Configure<BinanceOptions>(builder.Configuration.GetSection("Binance"));
builder.Services.AddHttpClient<BinanceRestClient>();
builder.AddRedisClient(connectionName: "redis"); // caches historical candles between backtest runs

// Every AddSingleton<IStrategy>(...) here is one more strategy the /api/backtest
// endpoint runs against the same historical data and returns a result for —
// this is how the tester runs and compares many strategies simultaneously.
builder.Services.AddSingleton<IStrategy>(new TradingRobot.Strategies.SmaCrossStrategy());

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles(); // serves wwwroot/index.html — the backtest chart
app.MapBacktestEndpoints();

app.Run();
