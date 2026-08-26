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

public sealed record ScheduleSlotResponse(string Start, string End, bool IsAvailable, string? Title, bool IsMine);

public sealed record RoomScheduleResponse(string RoomId, int Capacity, ScheduleSlotResponse[] Slots);

public sealed record MyBookingResponse(string BookingId, string RoomId, string Title, string Start, string End, int Attendees);
