using RoomBooking.Agent;

namespace RoomBooking.Web.Auth;

/// <summary>
/// The signed-in user for the lifetime of one Blazor circuit. The chat page fills this from the
/// authentication state before the assistant is used.
///
/// Reading it before it is set throws rather than returning a default. A silent fallback here would
/// mean acting on the wrong person's bookings, which is exactly the failure this indirection exists
/// to prevent.
/// </summary>
public sealed class CurrentUser : IUserContext
{
    private string? _userId;

    public string UserId => _userId
        ?? throw new InvalidOperationException("The current user has not been established for this circuit.");

    public string Username { get; private set; } = string.Empty;

    public void Set(string userId, string username)
    {
        _userId = userId;
        Username = username;
    }
}
