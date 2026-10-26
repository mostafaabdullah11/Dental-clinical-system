using System.Text.RegularExpressions;

namespace Clinic.SharedKernel.Validation;

/// <summary>
/// FR-SEC-008 heuristic: Sales-authored text that Doctors will read (an appointment reason) must not carry a phone
/// number or an e-mail address. A heuristic, not a guarantee; it rejects the obvious cases.
/// </summary>
public static partial class ContactPatterns
{
    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.]+", RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    // Eight or more digits (Latin or Arabic-Indic), optionally separated by spaces, dots or dashes.
    [GeneratedRegex(@"(?:[0-9٠-٩][\s.\-]?){8,}", RegexOptions.CultureInvariant)]
    private static partial Regex Phone();

    public static bool ContainsContact(string? text) =>
        !string.IsNullOrWhiteSpace(text) && (Email().IsMatch(text) || Phone().IsMatch(text));
}
