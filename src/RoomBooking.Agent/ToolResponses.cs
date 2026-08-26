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

public sealed record AvailableRoomResponse(string RoomId, int Capacity, bool IsFree);

/// <summary>
/// Carries the problems alongside the rooms so that a request the tool refused to carry out cannot
/// be mistaken for a calendar with nothing free in it.
/// </summary>
public sealed record AvailabilityResponse(AvailableRoomResponse[] Rooms, string[] Problems);

public sealed record ScheduleSlotResponse(string Start, string End, bool IsAvailable, string? Title, bool IsMine);

/// <summary>
/// Problems for the same reason: an unreadable range and an unknown room are both refusals, and
/// neither may reach the user as a room with an empty diary.
/// </summary>
public sealed record RoomScheduleResponse(
    string RoomId, int Capacity, ScheduleSlotResponse[] Slots, string[] Problems);

public sealed record MyBookingResponse(string BookingId, string RoomId, string Title, string Start, string End, int Attendees);
