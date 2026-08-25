namespace RoomBooking.Core.Domain;

/// <summary>A meeting room in the Cubo Itaú office. Rooms are identified by a single letter, A through E.</summary>
public sealed class Room
{
    public required string Id { get; init; }

    /// <summary>
    /// Maximum number of attendees the room holds. The challenge states that capacities are
    /// room-specific but never gives the values; see doc/ for the assumed figures.
    /// </summary>
    public required int Capacity { get; init; }
}
