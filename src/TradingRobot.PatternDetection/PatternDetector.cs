using TradingRobot.Domain.Models;

namespace TradingRobot.PatternDetection;

// Curated v1 set (locked per Dashboard-Frontend-Requirements.md open items): a
// small handful of well-known single- and two-candle patterns, not an exhaustive
// textbook list. Shape-based only — no trend context (e.g. "hammer after a
// downtrend") is considered, which is a known simplification worth revisiting
// once this feeds anything beyond visual highlighting.
public sealed class PatternDetector
{
    public IReadOnlyList<PatternMatch> Detect(IReadOnlyList<Candle> candles)
    {
        var matches = new List<PatternMatch>();

        for (var i = 0; i < candles.Count; i++)
        {
            if (TryDoji(candles[i], out var doji)) matches.Add(new PatternMatch("Doji", i, i, doji));
            if (TryHammer(candles[i], out var hammer)) matches.Add(new PatternMatch("Hammer", i, i, hammer));
            if (TryShootingStar(candles[i], out var star)) matches.Add(new PatternMatch("Shooting star", i, i, star));

            if (i == 0) continue;

            if (TryBullishEngulfing(candles[i - 1], candles[i], out var bullEngulf))
                matches.Add(new PatternMatch("Bullish engulfing", i - 1, i, bullEngulf));
            if (TryBearishEngulfing(candles[i - 1], candles[i], out var bearEngulf))
                matches.Add(new PatternMatch("Bearish engulfing", i - 1, i, bearEngulf));
        }

        return matches;
    }

    private static bool TryDoji(Candle c, out string description)
    {
        description = "Open and close are nearly equal — indecision between buyers and sellers.";
        var range = c.High - c.Low;
        if (range <= 0) return false;
        var body = Math.Abs(c.Close - c.Open);
        return body / range <= 0.1m;
    }

    private static bool TryHammer(Candle c, out string description)
    {
        description = "Small body near the top with a long lower wick — rejection of lower prices.";
        var range = c.High - c.Low;
        if (range <= 0) return false;
        var body = Math.Abs(c.Close - c.Open);
        var bodyTop = Math.Max(c.Open, c.Close);
        var bodyBottom = Math.Min(c.Open, c.Close);
        var lowerWick = bodyBottom - c.Low;
        var upperWick = c.High - bodyTop;
        return body / range <= 0.3m && lowerWick >= body * 2 && upperWick <= body * 0.5m;
    }

    private static bool TryShootingStar(Candle c, out string description)
    {
        description = "Small body near the bottom with a long upper wick — rejection of higher prices.";
        var range = c.High - c.Low;
        if (range <= 0) return false;
        var body = Math.Abs(c.Close - c.Open);
        var bodyTop = Math.Max(c.Open, c.Close);
        var bodyBottom = Math.Min(c.Open, c.Close);
        var lowerWick = bodyBottom - c.Low;
        var upperWick = c.High - bodyTop;
        return body / range <= 0.3m && upperWick >= body * 2 && lowerWick <= body * 0.5m;
    }

    private static bool TryBullishEngulfing(Candle prev, Candle curr, out string description)
    {
        description = "A bullish candle's body fully engulfs the prior bearish candle's body.";
        var prevBearish = prev.Close < prev.Open;
        var currBullish = curr.Close > curr.Open;
        return prevBearish && currBullish
            && curr.Open <= prev.Close && curr.Close >= prev.Open;
    }

    private static bool TryBearishEngulfing(Candle prev, Candle curr, out string description)
    {
        description = "A bearish candle's body fully engulfs the prior bullish candle's body.";
        var prevBullish = prev.Close > prev.Open;
        var currBearish = curr.Close < curr.Open;
        return prevBullish && currBearish
            && curr.Open >= prev.Close && curr.Close <= prev.Open;
    }
}
