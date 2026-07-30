using TradingRobot.Dashboard.Web.Hubs;
using TradingRobot.Domain.Abstractions;
using TradingRobot.MarketData.BinanceNet;
using TradingRobot.PatternDetection;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddRedisClient(connectionName: "redis"); // signal stream reads + (later) live-execution-bot status

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

// v1 scope: charting/market-structure only (symbol dropdown, timeframe switch,
// candles, drawing tools, pattern highlighting, signal markers). Live execution
// status is the confirmed next addition, not part of this pass — see
// Dashboard-Frontend-Requirements.md "Sequencing".
builder.Services.AddBinanceNetMarketData();
builder.Services.AddSingleton<PatternDetector>();

// Same IStrategy registrations as SignalGenerator.Worker (see that Program.cs),
// used here only to compute signal markers on demand for a chosen historical date
// range — SignalGenerator.Worker's live Redis Stream has nothing for past dates.
// This never places orders or sends alerts; it's read-only historical evaluation.
builder.Services.AddSingleton<IStrategy>(new TradingRobot.Strategies.SmaCrossStrategy());

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(name: "default", pattern: "{controller=Dashboard}/{action=Index}/{id?}");
app.MapHub<CandleHub>("/hubs/candles");

app.Run();
