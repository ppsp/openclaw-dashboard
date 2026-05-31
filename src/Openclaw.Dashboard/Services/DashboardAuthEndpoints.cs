using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using Openclaw.Dashboard.Options;

namespace Openclaw.Dashboard.Services;

public static class DashboardAuthEndpoints
{
    public static IEndpointRouteBuilder MapDashboardAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/login", (
            HttpContext httpContext,
            IOptions<DashboardAuthOptions> options) =>
        {
            var returnUrl = NormalizeReturnUrl(httpContext.Request.Query["returnUrl"].ToString());
            var reason = httpContext.Request.Query["reason"].ToString();
            var error = reason switch
            {
                "invalid" => "The password did not match.",
                "config" => "Dashboard authentication is not configured.",
                _ => null
            };

            return Results.Content(
                RenderLoginPage(returnUrl, error, options.Value.Enabled),
                "text/html");
        }).AllowAnonymous();

        endpoints.MapPost("/login", async (
            HttpContext httpContext,
            IOptions<DashboardAuthOptions> options,
            DashboardPasswordHasher passwordHasher) =>
        {
            var form = await httpContext.Request.ReadFormAsync();
            var returnUrl = NormalizeReturnUrl(form["returnUrl"].ToString());
            var password = form["password"].ToString();
            var authOptions = options.Value;

            if (!authOptions.Enabled)
            {
                return Results.LocalRedirect(returnUrl);
            }

            if (string.IsNullOrWhiteSpace(authOptions.PasswordHash))
            {
                return Results.LocalRedirect($"/login?reason=config&returnUrl={Uri.EscapeDataString(returnUrl)}");
            }

            if (!passwordHasher.VerifyPassword(password, authOptions.PasswordHash))
            {
                return Results.LocalRedirect($"/login?reason=invalid&returnUrl={Uri.EscapeDataString(returnUrl)}");
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "Dashboard"),
                new Claim(ClaimTypes.Role, "DashboardUser")
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            return Results.LocalRedirect(returnUrl);
        }).AllowAnonymous();

        endpoints.MapGet("/logout", async (HttpContext httpContext) =>
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect("/login");
        }).AllowAnonymous();

        return endpoints;
    }

    private static string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)
            || !Uri.TryCreate(returnUrl, UriKind.Relative, out var uri)
            || returnUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return "/command-center";
        }

        return uri.ToString();
    }

    private static string RenderLoginPage(string returnUrl, string? error, bool enabled)
    {
        var encodedReturnUrl = WebUtility.HtmlEncode(returnUrl);
        var errorBlock = string.IsNullOrWhiteSpace(error)
            ? string.Empty
            : $"""<div class="alert" role="alert">{WebUtility.HtmlEncode(error)}</div>""";
        var disabledAttribute = enabled ? string.Empty : " disabled";

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>Openclaw Login</title>
                <style>
                    :root {
                        color-scheme: dark;
                        font-family: "Segoe UI", system-ui, sans-serif;
                        background: #070a12;
                        color: #f4fbff;
                    }

                    body {
                        min-height: 100vh;
                        margin: 0;
                        display: grid;
                        place-items: center;
                        background:
                            radial-gradient(circle at 18% 14%, rgba(0, 229, 255, 0.16), transparent 32rem),
                            radial-gradient(circle at 82% 78%, rgba(255, 43, 214, 0.12), transparent 30rem),
                            #070a12;
                    }

                    main {
                        width: min(92vw, 26rem);
                        padding: 2rem;
                        border: 1px solid #23364d;
                        border-radius: 8px;
                        background: #101827;
                        box-shadow: 0 1.5rem 4rem rgba(0, 0, 0, 0.35);
                    }

                    h1 {
                        margin: 0 0 0.5rem;
                        font-size: 1.6rem;
                        letter-spacing: 0;
                    }

                    p {
                        margin: 0 0 1.5rem;
                        color: #91a9bd;
                    }

                    label {
                        display: block;
                        margin-bottom: 0.45rem;
                        color: #d8f7ff;
                        font-size: 0.95rem;
                    }

                    input {
                        box-sizing: border-box;
                        width: 100%;
                        height: 2.8rem;
                        border: 1px solid #23364d;
                        border-radius: 6px;
                        background: #070a12;
                        color: #f4fbff;
                        padding: 0 0.9rem;
                        font: inherit;
                    }

                    button {
                        width: 100%;
                        height: 2.8rem;
                        margin-top: 1rem;
                        border: 0;
                        border-radius: 6px;
                        background: #00e5ff;
                        color: #061018;
                        font: inherit;
                        font-weight: 700;
                        cursor: pointer;
                    }

                    button:disabled {
                        cursor: not-allowed;
                        opacity: 0.55;
                    }

                    .alert {
                        margin-bottom: 1rem;
                        padding: 0.75rem;
                        border: 1px solid #ff4d7d;
                        border-radius: 6px;
                        color: #ffdce5;
                        background: rgba(255, 77, 125, 0.12);
                    }
                </style>
            </head>
            <body>
                <main>
                    <h1>Openclaw Mission Control</h1>
                    <p>Sign in to continue.</p>
                    {{errorBlock}}
                    <form method="post" action="/login" autocomplete="off">
                        <input type="hidden" name="returnUrl" value="{{encodedReturnUrl}}">
                        <label for="password">Dashboard password</label>
                        <input id="password" name="password" type="password" autocomplete="current-password" autofocus{{disabledAttribute}}>
                        <button type="submit"{{disabledAttribute}}>Sign in</button>
                    </form>
                </main>
            </body>
            </html>
            """;
    }
}
