using System.Security.Cryptography;
using System.Text;

namespace Breakpoint.Core;

public static class OwnerAuthentication
{
    private const int Iterations = 600_000;

    public static (OwnerSecrets Secrets, string RecoveryCode) Create(string pin)
    {
        ValidatePin(pin);
        var salt = RandomNumberGenerator.GetBytes(16);
        var recoverySalt = RandomNumberGenerator.GetBytes(16);
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
        return (new OwnerSecrets(Convert.ToBase64String(salt), Hash(pin, salt),
            Convert.ToBase64String(recoverySalt), Hash(code, recoverySalt)), code);
    }

    public static void ValidatePin(string pin)
    {
        if (pin.Length is < 6 or > 12 || pin.Any(c => c is < '0' or > '9'))
            throw new ArgumentException("Choose a PIN with 6–12 digits.");
    }

    public static bool VerifyPin(OwnerSecrets secrets, string? pin) =>
        Verify(pin ?? "", secrets.Salt, secrets.PinHash);
    public static bool VerifyRecovery(OwnerSecrets secrets, string? code) =>
        Verify((code ?? "").Replace("-", "").Replace(" ", "").ToUpperInvariant(), secrets.RecoverySalt, secrets.RecoveryHash);

    private static string Hash(string value, byte[] salt) => Convert.ToBase64String(
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(value), salt, Iterations, HashAlgorithmName.SHA256, 32));
    private static bool Verify(string value, string salt, string hash) => CryptographicOperations.FixedTimeEquals(
        Convert.FromBase64String(Hash(value, Convert.FromBase64String(salt))), Convert.FromBase64String(hash));
}
