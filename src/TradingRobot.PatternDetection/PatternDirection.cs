using TradingRobot.Domain.Models;

namespace TradingRobot.PatternDetection;

// Maps a detected pattern's name to the directional bias it implies, for anything
// that wants to turn a recognized shape into a Buy/Sell call rather than just a
// chart highlight. Deliberately kept separate from PatternDetector itself — the
// detector's job is "what shape is this," not "what should a trader do about it";
// see Dashboard-Frontend-Requirements.md "Signal generation — patterns vs
// indicators vs combined."
//
// Doji is intentionally excluded (returns null) — it signals indecision, not a
// direction, so it stays highlight-only and never becomes a signal on its own.
public static class PatternDirection
{
    public static OrderSide? For(string patternName) => patternName switch
    {
        "Hammer" => OrderSide.Buy,
        "Bullish engulfing" => OrderSide.Buy,
        "Shooting star" => OrderSide.Sell,
        "Bearish engulfing" => OrderSide.Sell,
        _ => null,
    };
}
