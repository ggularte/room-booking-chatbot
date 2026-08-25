using RoomBooking.Core.Domain;

namespace RoomBooking.Core.Bookings;

/// <summary>Outcome of a create request: either the stored booking, or the constraints it broke.</summary>
public sealed record BookingResult(Booking? Booking, IReadOnlyList<BookingError> Errors)
{
    public bool Succeeded => Errors.Count == 0 && Booking is not null;

    public static BookingResult Ok(Booking booking) => new(booking, []);
    public static BookingResult Failed(IReadOnlyList<BookingError> errors) => new(null, errors);
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

/// <summary>Whether a room is free across an entire requested range.</summary>
public sealed record RoomAvailability(string RoomId, int Capacity, bool IsFree);

/// <summary>One 30-minute slot and what, if anything, holds it.</summary>
public sealed record SlotStatus(DateTime Start, DateTime End, bool IsAvailable, string? Title, string? BookedByUserId);

public sealed record RoomSchedule(string RoomId, int Capacity, IReadOnlyList<SlotStatus> Slots);
