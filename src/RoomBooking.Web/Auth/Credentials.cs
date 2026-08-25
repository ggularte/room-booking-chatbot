namespace RoomBooking.Web.Auth;

/// <summary>
/// The two accounts the challenge defines, both sharing one password. The password is a fixture
/// from the challenge document rather than a secret, so it sits in configuration where a reviewer
/// can find it, and can still be overridden by an environment variable.
/// </summary>
public sealed class Credentials
{
    public const string SectionName = "Auth";

    public string Password { get; init; } = "TechnicalChallengePromtior";

    private static readonly Dictionary<string, string> UserIdsByName =
        new(StringComparer.OrdinalIgnoreCase) { ["User1"] = "user1", ["User2"] = "user2" };

    /// <summary>Returns the user id for a valid pair, or null when either half does not match.</summary>
    public (string UserId, string Username)? Verify(string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(username) || password != Password)
            return null;

        return UserIdsByName.TryGetValue(username.Trim(), out var id)
            ? (id, char.ToUpperInvariant(username.Trim()[0]) + username.Trim()[1..].ToLowerInvariant())
            : null;
    }
}
