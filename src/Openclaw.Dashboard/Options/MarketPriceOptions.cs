namespace Openclaw.Dashboard.Options;

public sealed class MarketPriceOptions
{
    public const string SectionName = "MarketPrices";

    public string Provider { get; set; } = "Yahoo";

    public int CacheMinutes { get; set; } = 15;

    public int RequestTimeoutSeconds { get; set; } = 10;
}
