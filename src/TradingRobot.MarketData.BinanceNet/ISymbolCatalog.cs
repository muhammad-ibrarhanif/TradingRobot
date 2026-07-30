namespace TradingRobot.MarketData.BinanceNet;

// Deliberately separate from IMarketDataProvider (in TradingRobot.Domain) rather
// than added to it — listing tradable symbols is a Dashboard-only need for the
// symbol dropdown, not something the Strategy Tester / Signal Generator / Live
// Execution Bot need, so the shared core interface stays untouched.
public interface ISymbolCatalog
{
    Task<IReadOnlyList<string>> GetAvailableSymbolsAsync(CancellationToken ct = default);
}
