using TradingRobot.Domain.Abstractions;
using TradingRobot.MarketData.BinanceNet;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    // `AddBinance()` is Binance.Net's own registration helper (from the
    // CryptoExchange.Net base library it's built on) — it wires up
    // IBinanceRestClient/IBinanceSocketClient. This just layers our provider
    // on top so Dashboard.Web can depend on IMarketDataProvider/ISymbolCatalog
    // without knowing Binance.Net is behind them.
    public static IServiceCollection AddBinanceNetMarketData(this IServiceCollection services)
    {
        services.AddBinance();
        services.AddSingleton<BinanceNetMarketDataProvider>();
        services.AddSingleton<IMarketDataProvider>(sp => sp.GetRequiredService<BinanceNetMarketDataProvider>());
        services.AddSingleton<ISymbolCatalog>(sp => sp.GetRequiredService<BinanceNetMarketDataProvider>());
        return services;
    }
}
