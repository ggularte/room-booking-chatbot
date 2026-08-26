using RoomBooking.Core.Domain;

namespace RoomBooking.Core.Bookings;

/// <summary>
/// The booking constraints from the challenge, as pure functions over the request and the
/// bookings already held for the room. Deliberately free of persistence and of the assistant:
/// these rules are what the system guarantees, independent of anything the model decides to say.
/// </summary>
public static class BookingRules
{
    public const int SlotMinutes = 30;
    public const int MaxDurationMinutes = 180;

    /// <summary>
    /// Matches the column width. SQLite does not enforce declared lengths, so without this a title
    /// is unbounded — and it is read back into the assistant's context every time someone asks for
    /// a room's schedule, where an enormous one crowds out the conversation.
    /// </summary>
    public const int MaxTitleLength = 200;

    /// <summary>
    /// How far ahead a room can be held. Nothing in the challenge sets a horizon, which left the
    /// year 9999 as bookable — junk that would sit in the owner's list forever. A year is generous
    /// for a meeting room and keeps obvious nonsense out.
    /// </summary>
    public static readonly TimeSpan BookingHorizon = TimeSpan.FromDays(365);

    /// <summary>
    /// Validates a booking request and returns every constraint it breaks. All violations are
    /// reported at once so the assistant can tell the user everything that is wrong in one turn
    /// rather than dripping one correction per message.
    /// </summary>
    /// <param name="room">The target room, or null when no room matched the requested id.</param>
    /// <param name="existingInRoom">Bookings already held for that room.</param>
    /// <param name="now">
    /// The current moment, passed in rather than read from a clock so these stay pure functions.
    /// </param>
    /// <param name="ignoreBookingId">Booking to exclude from the overlap check, when rescheduling.</param>
    public static IReadOnlyList<BookingError> Validate(
        Room? room,
        string? title,
        DateTime start,
        DateTime end,
        int attendees,
        IEnumerable<Booking> existingInRoom,
        DateTime now,
        Guid? ignoreBookingId = null)
    {
        var errors = new List<BookingError>();

        if (string.IsNullOrWhiteSpace(title))
            errors.Add(BookingError.TitleRequired);
        else if (title.Trim().Length > MaxTitleLength)
            errors.Add(BookingError.TitleTooLong);

        if (room is null)
            errors.Add(BookingError.RoomNotFound);

        if (end <= start)
        {
            errors.Add(BookingError.EndNotAfterStart);
        }
        else if ((end - start).TotalMinutes > MaxDurationMinutes)
        {
            errors.Add(BookingError.ExceedsMaxDuration);
        }

        if (!IsAlignedToSlot(start) || !IsAlignedToSlot(end))
            errors.Add(BookingError.NotAlignedToSlot);

        if (attendees < 1)
            errors.Add(BookingError.AttendeesMustBePositive);
        else if (room is not null && attendees > room.Capacity)
            errors.Add(BookingError.ExceedsRoomCapacity);

        // Only bookings that have already finished are refused. A meeting that started ten minutes
        // ago is still a meeting someone may need to put on the calendar, and the challenge says
        // nothing about the past, so the narrower rule is the defensible one.
        if (end > start && end <= now)
            errors.Add(BookingError.EndsInThePast);

        if (end > start && start > now.Add(BookingHorizon))
            errors.Add(BookingError.TooFarAhead);

        if (end > start && Overlaps(start, end, existingInRoom, ignoreBookingId))
            errors.Add(BookingError.OverlapsExistingBooking);

        return errors;
    }

    /// <summary>A slot boundary falls on the hour or on the half hour, with no stray seconds.</summary>
    public static bool IsAlignedToSlot(DateTime moment) =>
        moment.Minute % SlotMinutes == 0 &&
        moment.Second == 0 &&
        moment.Millisecond == 0 &&
        moment.Ticks % TimeSpan.TicksPerMillisecond == 0;

    /// <summary>
    /// Two ranges conflict when each starts before the other ends. Touching ranges do not
    /// conflict: 10:00-11:30 leaves 11:30-12:00 free, which is the example the challenge gives.
    /// </summary>
    public static bool Overlaps(
        DateTime start,
        DateTime end,
        IEnumerable<Booking> existingInRoom,
        Guid? ignoreBookingId = null) =>
        existingInRoom.Any(b =>
            b.Id != ignoreBookingId &&
            start < b.End &&
            b.Start < end);

    /// <summary>Enumerates the 30-minute slot starts covered by a range.</summary>
    public static IEnumerable<DateTime> SlotsIn(DateTime start, DateTime end)
    {
        for (var slot = start; slot < end; slot = slot.AddMinutes(SlotMinutes))
            yield return slot;
    }
}
