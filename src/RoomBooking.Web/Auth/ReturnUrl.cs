namespace RoomBooking.Web.Auth;

/// <summary>
/// Where to send a visitor once they have signed in.
///
/// RedirectToLogin puts the page they were after in the query string, and a query string is
/// anyone's to write. A sign-in page that forwards wherever it is told is a phishing step with a
/// genuine login form in front of it, so only paths within this app are followed.
/// </summary>
public static class ReturnUrl
{
    public const string Fallback = "/";

    public static string Safe(string? candidate)
    {
        var target = candidate?.Trim();

        // Rejects absolute URLs, foreign schemes, and the backslash spellings a browser folds into
        // "//" — none of those are well-formed relative references.
        if (string.IsNullOrEmpty(target) || !Uri.IsWellFormedUriString(target, UriKind.Relative))
            return Fallback;

        // "//evil.example" is relative as far as Uri is concerned. A browser reads it as a
        // network-path reference, borrows the current scheme and leaves the origin.
        if (target.StartsWith("//", StringComparison.Ordinal))
            return Fallback;

        return target;
    }
}
