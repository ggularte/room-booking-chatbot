using RoomBooking.Core.Domain;

namespace RoomBooking.Core.Bookings;

/// <summary>Outcome of a create request: either the stored booking, or the constraints it broke.</summary>
public sealed record BookingResult(Booking? Booking, IReadOnlyList<BookingProblem> Errors)
{
    public bool Succeeded => Errors.Count == 0 && Booking is not null;

    public static BookingResult Ok(Booking booking) => new(booking, []);
    public static BookingResult Failed(IReadOnlyList<BookingProblem> errors) => new(null, errors);
}

public enum CancelError
{
    BookingNotFound,
    NotOwnedByUser,
}

public sealed record CancelResult(CancelError? Error)
{
    public bool Succeeded => Error is null;

    public static CancelResult Ok() => new((CancelError?)null);
    public static CancelResult Failed(CancelError error) => new(error);
}

/// <summary>A stretch of a room's day that is already spoken for.</summary>
public sealed record BusyPeriod(DateTime Start, DateTime End);

/// <summary>
/// Whether a room is free across an entire requested range, and when it is not.
///
/// The periods are reported because "busy for part of that range" leaves the asker no better off
/// than before they asked. When a room is busy is ordinary booking information — it is what
/// get_room_schedule already shows anyone — whereas what the meeting is called is not.
/// </summary>
public sealed record RoomAvailability(
    string RoomId, int Capacity, bool IsFree, IReadOnlyList<BusyPeriod> Busy);

/// <summary>One 30-minute slot and what, if anything, holds it.</summary>
public sealed record SlotStatus(DateTime Start, DateTime End, bool IsAvailable, string? Title, string? BookedByUserId);

public sealed record RoomSchedule(string RoomId, int Capacity, IReadOnlyList<SlotStatus> Slots);
