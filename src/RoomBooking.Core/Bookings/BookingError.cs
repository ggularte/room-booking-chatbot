namespace RoomBooking.Core.Bookings;

/// <summary>
/// Why a booking request was rejected. These are returned to the assistant as structured
/// values rather than prose so that the model reports a real outcome instead of inventing one.
/// </summary>
public enum BookingError
{
    TitleRequired,
    RoomNotFound,
    EndNotAfterStart,
    NotAlignedToSlot,
    ExceedsMaxDuration,
    AttendeesMustBePositive,
    ExceedsRoomCapacity,
    OverlapsExistingBooking,
}
