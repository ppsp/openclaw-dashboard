using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Openclaw.Dashboard.Options;

namespace Openclaw.Dashboard.Services;

public sealed class AdminWriteGuard(IOptions<ProductionSecurityOptions> options)
{
    public void RequireToken(string? suppliedToken)
    {
        var configuredToken = options.Value.AdminToken;
        if (string.IsNullOrWhiteSpace(configuredToken))
        {
            throw new InvalidOperationException("Admin writes are disabled until ProductionSecurity:AdminToken is configured.");
        }

        if (string.IsNullOrWhiteSpace(suppliedToken) || !TokenEquals(configuredToken, suppliedToken))
        {
            throw new UnauthorizedAccessException("A valid admin token is required for this write action.");
        }
    }

    private static bool TokenEquals(string expected, string supplied)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected.Trim());
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied.Trim());

        return expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
