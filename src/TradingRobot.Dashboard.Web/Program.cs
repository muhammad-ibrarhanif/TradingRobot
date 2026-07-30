var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddRedisClient(connectionName: "redis"); // live-execution-bot status/positions read from here

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseDefaultFiles();
app.UseStaticFiles();

// Placeholder: proxy/aggregate calls to strategy-tester + live-execution-bot go here.
// This is the seed of the eventual full TradingView-clone frontend — multi-timeframe
// charts, drawing tools, custom indicators, and the live execution status view
// all land in this project as they're built.
app.MapGet("/api/status", () => Results.Ok(new { status = "Dashboard.Web stub — wire up live-execution-bot status here." }));

app.Run();
