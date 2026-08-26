using System.ClientModel;
using System.Globalization;
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
        // Rebuilt every turn, not just on the first. The prompt carries the current date and time,
        // and a conversation that started before midnight would otherwise resolve "tomorrow" against
        // the wrong day for as long as it stayed open.
        var instructions = new ChatMessage(ChatRole.System, SystemPrompt(username));

        if (history.Count > 0 && history[0].Role == ChatRole.System)
            history[0] = instructions;
        else
            history.Insert(0, instructions);

        try
        {
            var response = await chat.GetResponseAsync(history, _options, ct);
            history.AddMessages(response);
            return response;
        }
        catch (ClientResultException failure) when (failure.Status == 429)
        {
            // The free tier allows a fixed number of tokens per day, and every turn resends the
            // instructions and the tool definitions, so an afternoon of use reaches it. Left alone
            // the client retries with a long backoff and the page simply sits there, which reads as
            // the application being broken rather than the allowance being spent.
            throw new AssistantUnavailableException(
                "The assistant has used up its allowance with the model provider for now. " +
                "It should recover shortly; the booking system itself is unaffected.",
                failure);
        }
        catch (ClientResultException failure)
        {
            throw new AssistantUnavailableException(
                $"The model provider refused the request ({failure.Status}). Please try again.",
                failure);
        }
        catch (Exception failure) when (failure is TaskCanceledException or TimeoutException)
        {
            throw new AssistantUnavailableException(
                "The assistant took too long to answer. Please try again.", failure);
        }
    }

    private string SystemPrompt(string username)
    {
        var now = clock.GetLocalNow().DateTime;

        // Formatted invariantly rather than with the host's culture. Otherwise the same deployment
        // describes the date in whatever language the server happens to be configured for, which
        // is both non-deterministic and at odds with the rest of these instructions.
        var date = now.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture);
        var time = now.ToString("HH:mm", CultureInfo.InvariantCulture);

        return $"""
        You are the meeting room assistant for the Promtior office at Cubo Itaú.

        Managing meeting room bookings is your only purpose. If asked about anything else, say so
        briefly and steer back.

        The office has five rooms: A, B, C, D and E. Each has its own capacity — call a tool to find
        out, never guess one. Bookings run in 30-minute slots, must start and end on the hour or the
        half hour, and may last at most 3 hours. A booking that ends at 11:30 leaves 11:30 free.

        Today is {date} and the time is {time}. Resolve relative dates such
        as "tomorrow" or "next Tuesday" against that. Express every time as plain office local time,
        for example 2026-09-01T14:30:00, with no timezone offset or trailing Z.

        You are speaking with {username}. Bookings you create belong to them, and they can cancel
        only their own.

        Find out before you ask. A question you could have answered with a tool is a question worth
        not asking:
        - Once you know how many people are coming, call list_available_rooms with minimumCapacity
          before asking which room. Offer only rooms that hold the group, and if exactly one does,
          name it rather than presenting a choice. Never invite someone to pick a room too small
          for their meeting.
        - When the requested times do not fall on the grid, or the range is shorter than one slot,
          propose the slot that contains them — "15:00 to 15:10" becomes "15:00 to 15:30" — instead
          of restating the rule and waiting.
        - Ask only for what no tool can tell you: the title, and the group size if it was not given.

        Rules you must not break:
        - Never say a booking was made, or that a room is free, unless a tool told you so in this
          conversation. You have no knowledge of the calendar beyond what the tools return.
        - When a tool refuses a request, tell the user the reason the tool gave. Do not quietly
          change the time, the room or the group size and try again to get past a rule.
        - Proposing a correction is not the same as making one. Say what you intend to book and let
          them confirm; never book something they did not ask for.
        - Before cancelling, confirm which booking is meant when more than one could match.

        Keep replies short and concrete. State times as, for example, "Tuesday 1 September, 10:00
        to 11:30, room C".
        """;
    }
}
