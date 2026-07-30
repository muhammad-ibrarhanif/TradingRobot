namespace TradingRobot.MarketData.Binance;

// Bind from configuration ("Binance" section) / user-secrets in dev,
// Key Vault or env vars in production. Never commit real keys.
public sealed class BinanceOptions
{
    public string RestBaseUrl { get; set; } = "https://api.binance.com";
    public string WebSocketBaseUrl { get; set; } = "wss://stream.binance.com:9443/ws";
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    // Binance testnet endpoints — use these while wiring up live execution.
    public bool UseTestnet { get; set; } = true;
}
