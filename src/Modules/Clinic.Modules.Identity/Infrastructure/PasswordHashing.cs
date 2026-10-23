using Clinic.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Clinic.Modules.Identity.Infrastructure;

/// <summary>
/// ASP.NET Core Identity password hasher: PBKDF2-HMAC-SHA512, per-user salt, ≥ 210,000 iterations
/// (SECURITY_DESIGN.md §2). Hashes are one-way; they are never returned, logged or audited.
/// </summary>
internal sealed class PasswordHashing
{
    public const int Iterations = 210_000;

    private readonly PasswordHasher<User> hasher = new(Options.Create(new PasswordHasherOptions
    {
        CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
        IterationCount = Iterations,
    }));

    private readonly Lazy<string> dummyHash;

    public PasswordHashing()
    {
        dummyHash = new Lazy<string>(() => hasher.HashPassword(null!, Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(24))));
    }

    public string Hash(string password) => hasher.HashPassword(null!, password);

    public PasswordVerificationResult Verify(string hash, string password) =>
        hasher.VerifyHashedPassword(null!, hash, password);

    /// <summary>Same work as a real verification so timing never reveals an unknown username.</summary>
    public void VerifyDummy(string password) => hasher.VerifyHashedPassword(null!, dummyHash.Value, password);
}
