using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Openclaw.Dashboard.Components;
using Openclaw.Dashboard.Data.Dashboard;
using Openclaw.Dashboard.Data.Portfolio;
using Openclaw.Dashboard.Data.Signals;
using Openclaw.Dashboard.Options;
using Openclaw.Dashboard.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.Configure<OpenclawPathsOptions>(
    builder.Configuration.GetSection(OpenclawPathsOptions.SectionName));

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
builder.Services.AddScoped<SignalQueryService>();
builder.Services.AddScoped<SignalReviewService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

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
