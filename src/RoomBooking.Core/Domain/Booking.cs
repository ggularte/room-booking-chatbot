namespace RoomBooking.Core.Domain;

/// <summary>
/// A reservation of one room over a contiguous range of 30-minute slots.
/// <see cref="Start"/> is inclusive and <see cref="End"/> is exclusive, so a booking
/// ending at 11:30 does not conflict with one starting at 11:30.
/// </summary>
public sealed class Booking
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string RoomId { get; init; }
    public required string UserId { get; init; }
    public required string Title { get; init; }
    public required DateTime Start { get; init; }
    public required DateTime End { get; init; }
    public required int Attendees { get; init; }

    public TimeSpan Duration => End - Start;
}
