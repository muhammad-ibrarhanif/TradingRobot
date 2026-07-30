namespace TradingRobot.Dashboard.Web.Models;

// Initial state handed to the view — everything after first paint (symbol
// switches, timeframe switches, live updates) happens client-side via the API
// endpoints below and the SignalR hub, per Dashboard-Frontend-Requirements.md.
public sealed record DashboardViewModel(
    string DefaultSymbol,
    IReadOnlyList<string> Timeframes);
