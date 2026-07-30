using TradingRobot.Dashboard.Web.Hubs;
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

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(name: "default", pattern: "{controller=Dashboard}/{action=Index}/{id?}");
app.MapHub<CandleHub>("/hubs/candles");

app.Run();
