namespace RoomBooking.Core.Domain;

/// <summary>One of the two users the challenge defines: User1 and User2.</summary>
public sealed class AppUser
{
    public required string Id { get; init; }
    public required string Username { get; init; }
}
