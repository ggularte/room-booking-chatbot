using Microsoft.Extensions.AI;

namespace RoomBooking.Agent;

/// <summary>
/// Wraps the chat model with the booking tools and the instructions that keep it inside its remit.
/// The tool-invocation loop itself is handled by <c>UseFunctionInvocation()</c> on the pipeline.
/// </summary>
public sealed class BookingAssistant(IChatClient chat, BookingTools tools, TimeProvider clock)
{
    private readonly ChatOptions _options = new()
    {
        Tools =
        [
            AIFunctionFactory.Create(tools.CreateBookingAsync, "create_booking"),
            AIFunctionFactory.Create(tools.ListAvailableRoomsAsync, "list_available_rooms"),
            AIFunctionFactory.Create(tools.GetRoomScheduleAsync, "get_room_schedule"),
            AIFunctionFactory.Create(tools.CancelBookingAsync, "cancel_booking"),
            AIFunctionFactory.Create(tools.ListMyBookingsAsync, "list_my_bookings"),
        ],
    };

    /// <summary>
    /// Continues a conversation. <paramref name="history"/> is mutated with the assistant's reply
    /// and with any tool calls made along the way, so the caller keeps the full trace.
    /// </summary>
    public async Task<ChatResponse> ContinueAsync(
        List<ChatMessage> history, string username, CancellationToken ct = default)
    {
        if (history.Count == 0 || history[0].Role != ChatRole.System)
            history.Insert(0, new ChatMessage(ChatRole.System, SystemPrompt(username)));

        var response = await chat.GetResponseAsync(history, _options, ct);
        history.AddMessages(response);
        return response;
    }

    private string SystemPrompt(string username)
    {
        var today = clock.GetLocalNow().DateTime;

        return $"""
        You are the meeting room assistant for the Promtior office at Cubo Itaú.

        Managing meeting room bookings is your only purpose. If asked about anything else, say so
        briefly and steer back.

        The office has five rooms: A, B, C, D and E. Each has its own capacity — call a tool to find
        out, never guess one. Bookings run in 30-minute slots, must start and end on the hour or the
        half hour, and may last at most 3 hours. A booking that ends at 11:30 leaves 11:30 free.

        Today is {today:dddd, d MMMM yyyy} and the time is {today:HH:mm}. Resolve relative dates such
        as "tomorrow" or "next Tuesday" against that. Express every time as plain office local time,
        for example 2026-09-01T14:30:00, with no timezone offset or trailing Z.

        You are speaking with {username}. Bookings you create belong to them, and they can cancel
        only their own.

        Rules you must not break:
        - Never say a booking was made, or that a room is free, unless a tool told you so in this
          conversation. You have no knowledge of the calendar beyond what the tools return.
        - When a tool refuses a request, tell the user the reason the tool gave. Do not quietly
          change the time, the room or the group size and try again to get past a rule.
        - If the room, the time range, the title or the attendee count is missing, ask for it. Do
          not invent a value or substitute a default.
        - Before cancelling, confirm which booking is meant when more than one could match.

        Keep replies short and concrete. State times as, for example, "Tuesday 1 September, 10:00
        to 11:30, room C".
        """;
    }
}
