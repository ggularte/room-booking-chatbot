namespace RoomBooking.Agent;

/// <summary>
/// What the tools hand back to the model. These are plain data, serialised into the conversation:
/// the model reports them, it does not decide them.
/// </summary>
/// <summary>
/// On success this echoes back what was actually stored, so the assistant describes the booking
/// from the record rather than from its recollection of the request.
/// </summary>
public sealed record CreateBookingResponse(
    bool Success,
    string? BookingId,
    string[] Problems,
    string? RoomId = null,
    string? Start = null,
    string? End = null,
    string? Title = null,
    int? Attendees = null);

public sealed record CancelBookingResponse(bool Success, string? Problem);

/// <summary>
/// A room's standing for the requested range. <see cref="Reason"/> is null when the room can be
/// booked, and otherwise says in words why it cannot.
/// </summary>
public sealed record AvailableRoomResponse(string RoomId, int Capacity, string? Reason);

/// <summary>
/// Rooms sorted into those that can be booked and those that cannot, each unusable one carrying its
/// reason.
///
/// Two named lists rather than one list of flags: given rooms marked <c>IsFree</c> and
/// <c>FitsGroup</c>, the model read four false flags as "nothing is available" and reported that to
/// the user while a suitable room sat free. Deciding which rooms qualify is arithmetic, and
/// arithmetic belongs here.
///
/// Problems travel alongside so that a request the tool could not carry out is never mistaken for
/// an office with nothing free in it.
/// </summary>
public sealed record AvailabilityResponse(
    AvailableRoomResponse[] Suitable,
    AvailableRoomResponse[] Unsuitable,
    string[] Problems);

public sealed record ScheduleSlotResponse(string Start, string End, bool IsAvailable, string? Title, bool IsMine);

/// <summary>
/// Problems for the same reason: an unreadable range and an unknown room are both refusals, and
/// neither may reach the user as a room with an empty diary.
/// </summary>
public sealed record RoomScheduleResponse(
    string RoomId, int Capacity, ScheduleSlotResponse[] Slots, string[] Problems);

public sealed record MyBookingResponse(string BookingId, string RoomId, string Title, string Start, string End, int Attendees);
