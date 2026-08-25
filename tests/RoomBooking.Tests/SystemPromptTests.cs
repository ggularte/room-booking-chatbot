using Microsoft.Extensions.AI;
using RoomBooking.Agent;
using RoomBooking.Core.Bookings;

namespace RoomBooking.Tests;

/// <summary>
/// The instructions carry the current date and time, so when they are built matters as much as what
/// they say. These run against a stand-in model: no network, no key.
/// </summary>
public class SystemPromptTests
{
    private sealed class NoUser : IUserContext
    {
        public string UserId => "user1";
    }

    private static BookingAssistant Assistant(RecordingChatClient chat, FixedClock clock, BookingService service) =>
        new(chat, new BookingTools(service, new NoUser()), clock);

    private static (BookingAssistant assistant, RecordingChatClient chat, FixedClock clock) Build(DateTime now)
    {
        var chat = new RecordingChatClient();
        var clock = new FixedClock(now);
        var service = TestDatabase.NewService(clock);
        return (Assistant(chat, clock, service), chat, clock);
    }

    [Fact]
    public async Task States_the_current_date_and_time()
    {
        var (assistant, chat, _) = Build(new DateTime(2026, 9, 1, 14, 30, 0));

        await assistant.ContinueAsync([new ChatMessage(ChatRole.User, "hello")], "User1");

        Assert.Contains("Tuesday, 1 September 2026", chat.LastSystemPrompt);
        Assert.Contains("14:30", chat.LastSystemPrompt);
    }

    [Fact]
    public async Task Refreshes_the_time_on_every_turn()
    {
        // The prompt used to be built once and reused. A conversation open across midnight would
        // then resolve "tomorrow" against the day it started, for as long as it stayed open.
        var (assistant, chat, clock) = Build(new DateTime(2026, 9, 1, 23, 50, 0));
        List<ChatMessage> history = [new(ChatRole.User, "hello")];

        await assistant.ContinueAsync(history, "User1");
        Assert.Contains("1 September 2026", chat.LastSystemPrompt);

        clock.Now = new DateTime(2026, 9, 2, 0, 10, 0);
        history.Add(new ChatMessage(ChatRole.User, "and now?"));
        await assistant.ContinueAsync(history, "User1");

        Assert.Contains("2 September 2026", chat.LastSystemPrompt);
        Assert.Contains("00:10", chat.LastSystemPrompt);
    }

    [Fact]
    public async Task Keeps_exactly_one_set_of_instructions()
    {
        var (assistant, chat, _) = Build(new DateTime(2026, 9, 1, 9, 0, 0));
        List<ChatMessage> history = [new(ChatRole.User, "hello")];

        await assistant.ContinueAsync(history, "User1");
        history.Add(new ChatMessage(ChatRole.User, "again"));
        await assistant.ContinueAsync(history, "User1");

        Assert.Equal(1, chat.Calls.Last().Count(m => m.Role == ChatRole.System));
    }

    [Fact]
    public async Task Names_the_signed_in_user()
    {
        var (assistant, chat, _) = Build(new DateTime(2026, 9, 1, 9, 0, 0));

        await assistant.ContinueAsync([new ChatMessage(ChatRole.User, "hello")], "User2");

        Assert.Contains("User2", chat.LastSystemPrompt);
    }
}
