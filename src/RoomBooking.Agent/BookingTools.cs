using System.ComponentModel;
using System.Globalization;
using RoomBooking.Core.Bookings;

namespace RoomBooking.Agent;

/// <summary>
/// The tools the assistant may call. Each one is a thin translation between the model's arguments
/// and <see cref="BookingService"/>; none of them decides whether a booking is legal.
/// </summary>
public sealed class BookingTools(BookingService service, IUserContext user)
{
    [Description("Create a meeting room booking for the signed-in user. Times must fall on the hour or half hour, the booking may last at most 3 hours, and the attendee count must not exceed the room's capacity. Returns the problems found if the booking was refused.")]
    public async Task<CreateBookingResponse> CreateBookingAsync(
        [Description("Room letter: A, B, C, D or E.")] string roomId,
        [Description("Start of the booking in the office's local time, as 2026-09-01T10:00:00. Do not add a timezone offset.")] string start,
        [Description("End of the booking, exclusive, in the same format. A booking ending at 11:30 leaves 11:30 free.")] string end,
        [Description("Title of the appointment, e.g. 'Interview with John Doe'.")] string title,
        [Description("Number of people attending.")] int attendees,
        CancellationToken ct = default)
    {
        if (!TryReadMoment(start, out var from))
            return new CreateBookingResponse(false, null, [NotAMoment(nameof(start), start)]);

        if (!TryReadMoment(end, out var to))
            return new CreateBookingResponse(false, null, [NotAMoment(nameof(end), end)]);

        var result = await service.CreateBookingAsync(
            NormalizeRoom(roomId), from, to, title, attendees, user.UserId, ct);

        return result.Succeeded
            ? new CreateBookingResponse(
                true, result.Booking!.Id.ToString(), [],
                result.Booking.RoomId, Format(result.Booking.Start), Format(result.Booking.End),
                result.Booking.Title, result.Booking.Attendees)
            : new CreateBookingResponse(false, null, result.Errors.Select(Describe).ToArray());
    }

    [Description("Report which rooms can be booked over a time range and which cannot. Suitable rooms are free and large enough; unsuitable ones come with the reason, so it can be explained rather than guessed. Book from Suitable only. Returns the problems found if the range could not be read.")]
    public async Task<AvailabilityResponse> ListAvailableRoomsAsync(
        [Description("Start of the range in the office's local time, as 2026-09-01T10:00:00.")] string start,
        [Description("End of the range, in the same format.")] string end,
        [Description("How many people are coming, when known. Rooms too small are still returned, marked as not fitting.")] int? minimumCapacity = null,
        CancellationToken ct = default)
    {
        // Refused in words rather than as an empty list: nothing free and never looked are
        // different answers, and only one of them may be reported to the user as fact.
        if (!TryReadMoment(start, out var from))
            return new AvailabilityResponse([], [], [NotAMoment(nameof(start), start)]);

        if (!TryReadMoment(end, out var to))
            return new AvailabilityResponse([], [], [NotAMoment(nameof(end), end)]);

        // Queried unfiltered, then sorted here. Narrowing in the query would leave the assistant
        // able to say which room to use but not why the others were ruled out, and "the rest are
        // too small" and "the rest are taken" are different answers to the person asking.
        var rooms = await service.ListAvailableRoomsAsync(from, to, minimumCapacity: null, ct);

        var judged = rooms
            .Select(r => new AvailableRoomResponse(r.RoomId, r.Capacity, WhyNot(r, minimumCapacity)))
            .ToArray();

        return new AvailabilityResponse(
            [.. judged.Where(r => r.Reason is null)],
            [.. judged.Where(r => r.Reason is not null)],
            []);
    }

    [Description("Show a room's schedule slot by slot over a range, marking which 30-minute slots are free and which are taken. Returns the problems found if the room or the range could not be read.")]
    public async Task<RoomScheduleResponse> GetRoomScheduleAsync(
        [Description("Room letter: A, B, C, D or E.")] string roomId,
        [Description("Start of the range in the office's local time, as 2026-09-01T10:00:00.")] string from,
        [Description("End of the range, in the same format.")] string to,
        CancellationToken ct = default)
    {
        var room = NormalizeRoom(roomId);

        // As above: an empty schedule and a request that was never carried out must not arrive
        // looking alike.
        if (!TryReadMoment(from, out var windowStart))
            return NoSchedule(room, NotAMoment(nameof(from), from));

        if (!TryReadMoment(to, out var windowEnd))
            return NoSchedule(room, NotAMoment(nameof(to), to));

        var schedule = await service.GetRoomScheduleAsync(room, windowStart, windowEnd, ct);
        if (schedule is null)
            return NoSchedule(room, Describe(new BookingProblem(BookingError.RoomNotFound)));

        var slots = schedule.Slots
            .Select(s => new ScheduleSlotResponse(
                Format(s.Start), Format(s.End), s.IsAvailable,
                // Other people's meeting titles are not this user's business; only the fact that
                // the slot is taken is.
                s.BookedByUserId == user.UserId ? s.Title : null,
                s.BookedByUserId == user.UserId))
            .ToArray();

        return new RoomScheduleResponse(schedule.RoomId, schedule.Capacity, slots, []);
    }

    [Description("Cancel one of the signed-in user's own bookings. Call list_my_bookings first to find its id.")]
    public async Task<CancelBookingResponse> CancelBookingAsync(
        [Description("Identifier of the booking to cancel, as returned by list_my_bookings.")] string bookingId,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(bookingId, out var id))
            return new CancelBookingResponse(false, "That booking id is not valid.");

        var result = await service.CancelBookingAsync(id, user.UserId, ct);

        return result.Succeeded
            ? new CancelBookingResponse(true, null)
            : new CancelBookingResponse(false, result.Error switch
            {
                CancelError.BookingNotFound => "No booking exists with that id.",
                CancelError.NotOwnedByUser => "That booking belongs to another user and cannot be cancelled.",
                _ => "The booking could not be cancelled.",
            });
    }

    [Description("List the signed-in user's own bookings, so they can be referred to or cancelled.")]
    public async Task<MyBookingResponse[]> ListMyBookingsAsync(CancellationToken ct = default)
    {
        var bookings = await service.ListUserBookingsAsync(user.UserId, ct: ct);
        return bookings
            .Select(b => new MyBookingResponse(
                b.Id.ToString(), b.RoomId, b.Title, Format(b.Start), Format(b.End), b.Attendees))
            .ToArray();
    }

    /// <summary>
    /// Reads a moment the model wrote. Taken as text rather than as a DateTime parameter so that
    /// "the 34th of September" comes back as a refusal the model can act on, instead of a
    /// deserialisation failure it only learns about as "Function failed".
    ///
    /// RoundtripKind keeps the literal clock reading: models emit a trailing Z even when told not
    /// to, and letting that be converted would silently shift every booking by the host's offset.
    /// </summary>
    private static bool TryReadMoment(string? value, out DateTime moment)
    {
        moment = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return false;

        // The office runs in one time zone, so the reading on the clock is what matters.
        moment = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        return true;
    }

    private static RoomScheduleResponse NoSchedule(string roomId, string problem) =>
        new(roomId, 0, [], [problem]);

    /// <summary>Why a room cannot be booked, or null when it can.</summary>
    private static string? WhyNot(RoomAvailability room, int? attendees)
    {
        if (attendees is not null && room.Capacity < attendees)
            return $"holds only {room.Capacity}";

        if (room.IsFree)
            return null;

        // Named, not merely alluded to. "Busy for part of that range" leaves the asker exactly
        // where they were; the hours let them pick another time without asking again.
        var when = string.Join(" and ", room.Busy.Select(b => $"{b.Start:HH:mm}-{b.End:HH:mm}"));

        return $"already booked {when}";
    }

    private static string NotAMoment(string field, string? value) =>
        $"The {field} '{value}' is not a real date and time. Use the form 2026-09-01T10:00:00.";

    private static string NormalizeRoom(string roomId) => roomId.Trim().ToUpperInvariant();

    /// <summary>
    /// Includes the weekday rather than leaving the model to derive it. Asked to work out which day
    /// "tomorrow" falls on, it gets it wrong often enough to tell someone their meeting is on
    /// Tuesday when it is on Wednesday. Handing it the answer removes the arithmetic.
    ///
    /// Invariant, so the same deployment does not describe days in whatever language the host
    /// happens to be configured for.
    /// </summary>
    private static string Format(DateTime moment) =>
        moment.ToString("yyyy-MM-dd (dddd) HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// A refusal in words, carrying whatever figure or hours make it actionable. A reason the
    /// reader cannot act on is barely better than no reason: told only that a room is too small,
    /// they have to ask how small.
    /// </summary>
    private static string Describe(BookingProblem problem) => problem.Error switch
    {
        BookingError.TitleRequired => "The appointment needs a title.",
        BookingError.TitleTooLong => $"That title is too long; keep it under {BookingRules.MaxTitleLength} characters.",
        BookingError.RoomNotFound => "There is no such room. The office has rooms A, B, C, D and E.",
        BookingError.EndNotAfterStart => "The end time must come after the start time.",
        BookingError.NotAlignedToSlot => "Bookings run in 30-minute slots, so they must start and end on the hour or half hour.",
        BookingError.ExceedsMaxDuration => $"A single booking cannot run longer than {BookingRules.MaxDurationMinutes / 60} hours.",
        BookingError.AttendeesMustBePositive => "The booking needs at least one attendee.",
        BookingError.ExceedsRoomCapacity => $"That room holds only {problem.Detail}.",
        BookingError.OverlapsExistingBooking => $"That room is already booked {problem.Detail}.",
        BookingError.EndsInThePast => "That time has already passed, so it cannot be booked.",
        BookingError.TooFarAhead => $"Rooms can only be booked up to {BookingRules.BookingHorizon.Days} days ahead.",
        BookingError.CouldNotSecureTheSlot => "Someone else is booking that room right now. Try again in a moment.",
        _ => "The booking was refused.",
    };
}
