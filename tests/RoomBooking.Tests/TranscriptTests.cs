using Microsoft.Extensions.AI;
using RoomBooking.Web;

namespace RoomBooking.Tests;

public class TranscriptTests
{
    private static ChatMessage User(string text) => new(ChatRole.User, text);
    private static ChatMessage Says(string text) => new(ChatRole.Assistant, text);

    private static ChatMessage Calls(string name) =>
        new(ChatRole.Assistant, [new FunctionCallContent($"call-{name}", name, null)]);

    private static ChatMessage Result(string name) =>
        new(ChatRole.Tool, [new FunctionResultContent($"call-{name}", "done")]);

    [Fact]
    public void Gathers_every_call_of_a_turn_onto_the_reply_it_led_to()
    {
        // Two calls in one turn used to render as two boxes, each announcing "1 tool call", which
        // reads as the assistant having answered twice.
        var entries = Transcript.Build([
            new ChatMessage(ChatRole.System, "instructions"),
            User("which capacity has each room?"),
            Calls("list_available_rooms"),
            Result("list_available_rooms"),
            Calls("list_available_rooms"),
            Result("list_available_rooms"),
            Says("The capacities are..."),
        ]);

        Assert.Equal(2, entries.Count);
        Assert.True(entries[0].IsUser);

        var reply = entries[1];
        Assert.False(reply.IsUser);
        Assert.Equal(["list_available_rooms", "list_available_rooms"], reply.Tools);
    }

    [Fact]
    public void Keeps_each_turn_to_its_own_calls()
    {
        var entries = Transcript.Build([
            User("first"),
            Calls("list_available_rooms"), Result("list_available_rooms"),
            Says("here they are"),
            User("second"),
            Calls("create_booking"), Result("create_booking"),
            Says("booked"),
        ]);

        Assert.Equal(["list_available_rooms"], entries[1].Tools);
        Assert.Equal(["create_booking"], entries[3].Tools);
    }

    [Fact]
    public void Shows_calls_that_have_no_reply_behind_them_yet()
    {
        // The turn is still running, or it failed after the call was made.
        var entries = Transcript.Build([User("book it"), Calls("create_booking")]);

        Assert.Equal(["create_booking"], entries[^1].Tools);
        Assert.Equal(string.Empty, entries[^1].Text);
    }

    [Fact]
    public void Leaves_out_the_instructions_and_the_tool_results()
    {
        var entries = Transcript.Build([
            new ChatMessage(ChatRole.System, "instructions"),
            User("hello"),
            Calls("list_my_bookings"), Result("list_my_bookings"),
            Says("you have none"),
        ]);

        Assert.Equal(["hello", "you have none"], entries.Select(e => e.Text).ToArray());
    }

    [Fact]
    public void Cleans_up_a_name_the_model_mangled()
    {
        var entries = Transcript.Build([
            User("what do I have?"),
            Calls("list_my_bookings<|channel|>functions.list_my_bookings"),
            Says("one booking"),
        ]);

        Assert.Equal(["list_my_bookings"], entries[1].Tools);
    }

    [Fact]
    public void Handles_an_empty_conversation()
    {
        Assert.Empty(Transcript.Build([]));
    }
}
