using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using MudBlazor.Services;
using System.Net;
using Openclaw.Dashboard.Components;
using Openclaw.Dashboard.Data.Dashboard;
using Openclaw.Dashboard.Data.Portfolio;
using Openclaw.Dashboard.Data.Signals;
using Openclaw.Dashboard.Options;
using Openclaw.Dashboard.Services;

if (args is ["--generate-auth-hash", var password])
{
    Console.WriteLine(new DashboardPasswordHasher().HashPassword(password));
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var dashboardAuthOptions = builder.Configuration
    .GetSection(DashboardAuthOptions.SectionName)
    .Get<DashboardAuthOptions>() ?? new DashboardAuthOptions();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddMudServices();
builder.Services.Configure<OpenclawPathsOptions>(
    builder.Configuration.GetSection(OpenclawPathsOptions.SectionName));
builder.Services.Configure<ProductionSecurityOptions>(
    builder.Configuration.GetSection(ProductionSecurityOptions.SectionName));
builder.Services.Configure<DashboardAuthOptions>(
    builder.Configuration.GetSection(DashboardAuthOptions.SectionName));
builder.Services.Configure<DashboardTimeOptions>(
    builder.Configuration.GetSection(DashboardTimeOptions.SectionName));
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = string.IsNullOrWhiteSpace(dashboardAuthOptions.CookieName)
            ? ".OpenclawDashboard.Auth"
            : dashboardAuthOptions.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(Math.Max(1, dashboardAuthOptions.SessionHours));
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    var securityOptions = builder.Configuration
        .GetSection(ProductionSecurityOptions.SectionName)
        .Get<ProductionSecurityOptions>() ?? new ProductionSecurityOptions();

    options.ForwardedHeaders = ParseForwardedHeaders(securityOptions.AllowedForwardedHeaders);
    options.ForwardLimit = 1;

    options.KnownProxies.Clear();
    foreach (var proxy in securityOptions.KnownProxies)
    {
        if (IPAddress.TryParse(proxy, out var address))
        {
            options.KnownProxies.Add(address);
        }
    }

    options.KnownIPNetworks.Clear();
    foreach (var network in securityOptions.KnownNetworks)
    {
        if (TryParseNetwork(network, out var knownNetwork))
        {
            options.KnownIPNetworks.Add(knownNetwork);
        }
    }
});

var signalsConnectionString = builder.Configuration.GetConnectionString("SignalsDb")
    ?? throw new InvalidOperationException("Missing SignalsDb connection string.");
var portfolioConnectionString = builder.Configuration.GetConnectionString("PortfolioDb")
    ?? throw new InvalidOperationException("Missing PortfolioDb connection string.");
var dashboardConnectionString = builder.Configuration.GetConnectionString("DashboardDb")
    ?? throw new InvalidOperationException("Missing DashboardDb connection string.");

EnsureSqliteDirectoryExists(dashboardConnectionString, builder.Environment.ContentRootPath);

builder.Services.AddDbContextFactory<SignalsDbContext>(options =>
    options.UseSqlite(signalsConnectionString));
builder.Services.AddDbContextFactory<PortfolioDbContext>(options =>
    options.UseSqlite(portfolioConnectionString));
builder.Services.AddDbContextFactory<DashboardDbContext>(options =>
    options.UseSqlite(dashboardConnectionString));
builder.Services.AddScoped<DashboardSummaryService>();
builder.Services.AddScoped<CronHealthService>();
builder.Services.AddScoped<SignalQueryService>();
builder.Services.AddScoped<SignalReviewService>();
builder.Services.AddScoped<PaperTradeQueryService>();
builder.Services.AddScoped<AppSettingsService>();
builder.Services.AddScoped<AdminWriteGuard>();
builder.Services.AddScoped<CreatorSourceService>();
builder.Services.AddScoped<TickerWatchlistService>();
builder.Services.AddSingleton<DashboardPasswordHasher>();
builder.Services.AddSingleton<DashboardTimeService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await scope.ServiceProvider
            .GetRequiredService<SignalReviewService>()
            .EnsureSignalsQualitySchemaAsync();
        await scope.ServiceProvider
            .GetRequiredService<CreatorSourceService>()
            .EnsureSchemaAsync();
        await scope.ServiceProvider
            .GetRequiredService<TickerWatchlistService>()
            .EnsureSchemaAsync();
    }
    catch (Exception ex) when (ex is SqliteException or InvalidOperationException or IOException)
    {
        logger.LogWarning(ex, "Signal quality schema setup failed.");
    }
}

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapDashboardAuthEndpoints();

var razorComponents = app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (dashboardAuthOptions.Enabled)
{
    razorComponents.RequireAuthorization();
}

app.Run();

static ForwardedHeaders ParseForwardedHeaders(IEnumerable<string>? headerNames)
{
    var forwardedHeaders = ForwardedHeaders.None;

    foreach (var headerName in headerNames ?? [])
    {
        forwardedHeaders |= headerName.Trim().ToLowerInvariant() switch
        {
            "xforwardedfor" or "x-forwarded-for" => ForwardedHeaders.XForwardedFor,
            "xforwardedhost" or "x-forwarded-host" => ForwardedHeaders.XForwardedHost,
            "xforwardedproto" or "x-forwarded-proto" => ForwardedHeaders.XForwardedProto,
            "xforwardedprefix" or "x-forwarded-prefix" => ForwardedHeaders.XForwardedPrefix,
            _ => ForwardedHeaders.None
        };
    }

    return forwardedHeaders;
}

static bool TryParseNetwork(string network, out System.Net.IPNetwork knownNetwork)
{
    knownNetwork = default!;
    return System.Net.IPNetwork.TryParse(network, out knownNetwork);
}

static void EnsureSqliteDirectoryExists(string connectionString, string contentRootPath)
{
    var sqliteConnectionString = new SqliteConnectionStringBuilder(connectionString);
    var dataSource = sqliteConnectionString.DataSource;

    if (string.IsNullOrWhiteSpace(dataSource) || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    var dbPath = Path.IsPathRooted(dataSource)
        ? dataSource
        : Path.Combine(contentRootPath, dataSource);
    var dbDirectory = Path.GetDirectoryName(dbPath);

    if (!string.IsNullOrWhiteSpace(dbDirectory))
    {
        Directory.CreateDirectory(dbDirectory);
    }
}
