using System.Security.Cryptography;

namespace Openclaw.Dashboard.Services;

public sealed class DashboardPasswordHasher
{
    private const string FormatVersion = "v1";
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int DefaultIterations = 210_000;

    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return string.Join(
            '$',
            FormatVersion,
            DefaultIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        var parts = passwordHash.Split('$');
        if (parts is not [FormatVersion, var iterationsText, var saltText, var expectedHashText]
            || !int.TryParse(iterationsText, out var iterations)
            || iterations <= 0)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(saltText);
            var expectedHash = Convert.FromBase64String(expectedHashText);
            var suppliedHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
