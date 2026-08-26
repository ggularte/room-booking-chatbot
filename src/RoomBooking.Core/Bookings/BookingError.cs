namespace RoomBooking.Core.Bookings;

/// <summary>
/// Why a booking request was rejected. These are returned to the assistant as structured
/// values rather than prose so that the model reports a real outcome instead of inventing one.
/// </summary>
public enum BookingError
{
    TitleRequired,
    TitleTooLong,
    RoomNotFound,
    EndNotAfterStart,
    NotAlignedToSlot,
    ExceedsMaxDuration,
    AttendeesMustBePositive,
    ExceedsRoomCapacity,
    OverlapsExistingBooking,
    EndsInThePast,
    TooFarAhead,
    CouldNotSecureTheSlot,
}

/// <summary>
/// A refusal, with the fact that makes it actionable.
///
/// The reason alone is often not enough to act on: "that room does not hold that many people"
/// leaves the asker guessing how many it does hold, and "already booked during part of that range"
/// leaves them no better off than before they asked. The assistant can only be as specific as what
/// it is handed, so what it needs travels with the reason.
/// </summary>
/// <param name="Detail">The figure or the hours the message needs, or null when it needs neither.</param>
public sealed record BookingProblem(BookingError Error, string? Detail = null);
