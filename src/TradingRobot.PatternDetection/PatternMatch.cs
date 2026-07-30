namespace TradingRobot.PatternDetection;

// A detected pattern spans one or more candles — StartIndex/EndIndex (inclusive,
// into the same candle list passed to the detector) let a consumer highlight every
// candle involved, not just one, per Dashboard-Frontend-Requirements.md item 6.
public sealed record PatternMatch(string Name, int StartIndex, int EndIndex, string Description);
