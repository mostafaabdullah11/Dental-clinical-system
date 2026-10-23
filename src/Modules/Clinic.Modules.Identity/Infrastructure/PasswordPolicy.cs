using Clinic.Modules.Identity.Contracts;
using Clinic.SharedKernel.Errors;

namespace Clinic.Modules.Identity.Infrastructure;

/// <summary>
/// DEFAULT policy (SECURITY_DESIGN.md §2): minimum 12 characters and a deny-list of common / breached
/// passwords; no composition rules and no forced expiry. The upper bound is technical (hashing cost).
/// </summary>
internal sealed class PasswordPolicy : IPasswordPolicy
{
    public const int MinimumLength = 12;
    public const int MaximumLength = 128;

    public string? Check(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return FieldErrorCodes.Required;
        }

        if (password.Length < MinimumLength)
        {
            return FieldErrorCodes.TooShort;
        }

        if (password.Length > MaximumLength)
        {
            return FieldErrorCodes.TooLong;
        }

        return CommonPasswords.IsDenied(password) ? FieldErrorCodes.PasswordDenied : null;
    }
}

internal static class CommonPasswords
{
    // A compact deny-list of the most common passwords of length ≥ 12 plus trivial repetitions.
    private static readonly HashSet<string> Denied = new(StringComparer.OrdinalIgnoreCase)
    {
        "123456789012", "1234567890123", "12345678901234", "123456123456", "111111111111",
        "000000000000", "123123123123", "qwertyuiopas", "qwerty123456", "qwertyuiop12",
        "password1234", "password12345", "password123456", "passwordpassword", "Password@123",
        "Password@1234", "Passw0rd1234", "iloveyou1234", "administrator", "admin1234567",
        "adminadminadmin", "welcome12345", "welcome123456", "letmein12345", "abc123abc123",
        "abcdefghijkl", "abcdef123456", "1q2w3e4r5t6y", "1qaz2wsx3edc", "zaq12wsxcde3",
        "qazwsxedcrfv", "asdfghjkl123", "monkey123456", "dragon123456", "football1234",
        "baseball1234", "sunshine1234", "princess1234", "superman1234", "trustno11234",
        "changeme1234", "changemenow!", "clinic123456", "clinicclinic", "dentist12345",
        "dental123456", "egypt1234567", "cairo1234567", "manager12345", "manager123456",
        "doctor123456", "reception123", "sales1234567",
    };

    public static bool IsDenied(string password)
    {
        if (Denied.Contains(password))
        {
            return true;
        }

        // Reject a single repeated character or a strictly ascending/descending digit run.
        if (password.Distinct().Count() == 1)
        {
            return true;
        }

        return IsSequentialDigits(password);
    }

    private static bool IsSequentialDigits(string value)
    {
        if (!value.All(char.IsAsciiDigit))
        {
            return false;
        }

        var up = true;
        var down = true;
        for (var i = 1; i < value.Length; i++)
        {
            up &= (value[i] - '0') == ((value[i - 1] - '0' + 1) % 10);
            down &= (value[i] - '0') == ((value[i - 1] - '0' + 9) % 10);
        }

        return up || down;
    }
}
