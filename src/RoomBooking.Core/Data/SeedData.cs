using RoomBooking.Core.Domain;

namespace RoomBooking.Core.Data;

/// <summary>The fixed office layout and user list the challenge defines.</summary>
public static class SeedData
{
    /// <summary>
    /// The five rooms of the Cubo Itaú office.
    ///
    /// The challenge requires room-specific capacities but never states the values, so these are
    /// an assumption. The spread is deliberate: rooms small enough that a routine meeting exceeds
    /// them and rooms large enough that it does not, which is what makes the capacity rule
    /// observable. Changing them here is the only change required.
    /// </summary>
    public static readonly Room[] Rooms =
    [
        new() { Id = "A", Capacity = 4 },
        new() { Id = "B", Capacity = 6 },
        new() { Id = "C", Capacity = 8 },
        new() { Id = "D", Capacity = 12 },
        new() { Id = "E", Capacity = 20 },
    ];

    /// <summary>
    /// The two users the challenge defines. The shared password is an authentication concern and
    /// lives in configuration, not here.
    /// </summary>
    public static readonly AppUser[] Users =
    [
        new() { Id = "user1", Username = "User1" },
        new() { Id = "user2", Username = "User2" },
    ];
}
