using System.Security.Cryptography;
using SimpleStockFlow.Application.Ports.Outbound;

namespace SimpleStockFlow.Adapters.Security;

/// <summary>PBKDF2-SHA256 with a per-user salt. Stored format: {iterations}.{salt}.{hash}, base64.</summary>
internal sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    private static readonly Lazy<string> Decoy =
        new(() => new Pbkdf2PasswordHasher().Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    public string DecoyHash => Decoy.Value;

    public string Hash(string plainPassword)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(plainPassword, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string plainPassword, string storedHash)
    {
        var parts = storedHash.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
            return false;

        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(plainPassword, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
