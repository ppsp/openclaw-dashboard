namespace Openclaw.Dashboard.Options;

public sealed class ProductionSecurityOptions
{
    public const string SectionName = "ProductionSecurity";

    public string? AdminToken { get; set; }

    public string[] AllowedForwardedHeaders { get; set; } =
    [
        "XForwardedFor",
        "XForwardedProto",
        "XForwardedHost"
    ];

    public string[] KnownProxies { get; set; } = [];

    public string[] KnownNetworks { get; set; } = [];
}
