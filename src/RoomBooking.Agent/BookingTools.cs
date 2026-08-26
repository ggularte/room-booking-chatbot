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
        [Description("Start of the booking in the office's local time, e.g. 2026-09-01T10:00:00. Do not add a timezone offset.")] DateTime start,
        [Description("End of the booking, exclusive. A booking ending at 11:30 leaves 11:30 free.")] DateTime end,
        [Description("Title of the appointment, e.g. 'Interview with John Doe'.")] string title,
        [Description("Number of people attending.")] int attendees,
        CancellationToken ct = default)
    {
        var result = await service.CreateBookingAsync(
            NormalizeRoom(roomId), AsWallClock(start), AsWallClock(end), title, attendees, user.UserId, ct);

        return result.Succeeded
            ? new CreateBookingResponse(
                true, result.Booking!.Id.ToString(), [],
                result.Booking.RoomId, Format(result.Booking.Start), Format(result.Booking.End),
                result.Booking.Title, result.Booking.Attendees)
            : new CreateBookingResponse(false, null, result.Errors.Select(Describe).ToArray());
    }

    [Description("List the meeting rooms that are completely free across a time range, optionally only those large enough for a given number of people.")]
    public async Task<AvailableRoomResponse[]> ListAvailableRoomsAsync(
        [Description("Start of the range in the office's local time.")] DateTime start,
        [Description("End of the range.")] DateTime end,
        [Description("Only return rooms holding at least this many people. Omit if the group size is unknown.")] int? minimumCapacity = null,
        CancellationToken ct = default)
    {
        var rooms = await service.ListAvailableRoomsAsync(AsWallClock(start), AsWallClock(end), minimumCapacity, ct);
        return rooms.Select(r => new AvailableRoomResponse(r.RoomId, r.Capacity, r.IsFree)).ToArray();
    }

    [Description("Show a room's schedule slot by slot over a range, marking which 30-minute slots are free and which are taken.")]
    public async Task<RoomScheduleResponse?> GetRoomScheduleAsync(
        [Description("Room letter: A, B, C, D or E.")] string roomId,
        [Description("Start of the range in the office's local time.")] DateTime from,
        [Description("End of the range.")] DateTime to,
        CancellationToken ct = default)
    {
        var schedule = await service.GetRoomScheduleAsync(NormalizeRoom(roomId), AsWallClock(from), AsWallClock(to), ct);
        if (schedule is null)
            return null;

        var slots = schedule.Slots
            .Select(s => new ScheduleSlotResponse(
                Format(s.Start), Format(s.End), s.IsAvailable,
                // Other people's meeting titles are not this user's business; only the fact that
                // the slot is taken is.
                s.BookedByUserId == user.UserId ? s.Title : null,
                s.BookedByUserId == user.UserId))
            .ToArray();

        return new RoomScheduleResponse(schedule.RoomId, schedule.Capacity, slots);
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
    /// Models tend to emit a trailing Z even when told not to. The office runs in one time zone,
    /// so the literal clock reading is what matters — reinterpreting it as UTC would silently
    /// shift every booking.
    /// </summary>
    private static DateTime AsWallClock(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

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

    private static string Describe(BookingError error) => error switch
    {
        BookingError.TitleRequired => "The appointment needs a title.",
        BookingError.TitleTooLong => $"That title is too long; keep it under {BookingRules.MaxTitleLength} characters.",
        BookingError.RoomNotFound => "There is no such room. The office has rooms A, B, C, D and E.",
        BookingError.EndNotAfterStart => "The end time must come after the start time.",
        BookingError.NotAlignedToSlot => "Bookings run in 30-minute slots, so they must start and end on the hour or half hour.",
        BookingError.ExceedsMaxDuration => "A single booking cannot run longer than 3 hours.",
        BookingError.AttendeesMustBePositive => "The booking needs at least one attendee.",
        BookingError.ExceedsRoomCapacity => "That room does not hold that many people.",
        BookingError.OverlapsExistingBooking => "That room is already booked during part of that range.",
        BookingError.EndsInThePast => "That time has already passed, so it cannot be booked.",
        BookingError.CouldNotSecureTheSlot => "Someone else is booking that room right now. Try again in a moment.",
        _ => "The booking was refused.",
    };
}
