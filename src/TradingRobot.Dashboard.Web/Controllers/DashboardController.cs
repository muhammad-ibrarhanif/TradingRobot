using Microsoft.AspNetCore.Mvc;
using TradingRobot.Dashboard.Web.Models;

namespace TradingRobot.Dashboard.Web.Controllers;

public sealed class DashboardController : Controller
{
    public IActionResult Index()
    {
        var model = new DashboardViewModel(
            DefaultSymbol: "BTCUSDT",
            Timeframes: ["1m", "5m", "15m", "1h", "4h", "1d"]);

        return View(model);
    }
}
