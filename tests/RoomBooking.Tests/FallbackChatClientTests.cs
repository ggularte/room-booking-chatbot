using System.ClientModel;
using Microsoft.Extensions.AI;
using RoomBooking.Agent;

namespace RoomBooking.Tests;

/// <summary>
/// Groq's free tier allows a fixed number of tokens per day per model, so one can be spent while
/// the others are untouched. Without a fallback the assistant stops answering for everyone until
/// the allowance rolls over.
/// </summary>
public class FallbackChatClientTests
{
    private sealed class Scripted(string answer, Exception? failWith = null) : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        {
            Calls++;

            if (failWith is not null)
                throw failWith;

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public object? GetService(Type t, object? key = null) => null;
        public void Dispose() { }
    }

    private static Task<ChatResponse> Ask(IChatClient client) =>
        client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

    [Fact]
    public async Task Uses_the_second_model_when_the_first_has_spent_its_allowance()
    {
        var primary = new Scripted("", Rejected.With(429));
        var fallback = new Scripted("from the fallback");

        var response = await Ask(new FallbackChatClient(primary, fallback));

        Assert.Equal("from the fallback", response.Text);
        Assert.Equal(1, primary.Calls);
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task Leaves_the_second_model_alone_while_the_first_answers()
    {
        var primary = new Scripted("from the primary");
        var fallback = new Scripted("from the fallback");

        var response = await Ask(new FallbackChatClient(primary, fallback));

        Assert.Equal("from the primary", response.Text);
        Assert.Equal(0, fallback.Calls);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(401)]
    public async Task Does_not_fall_back_on_a_refusal_that_a_second_model_cannot_fix(int status)
    {
        // A bad key or a provider outage will greet the fallback identically. Trying it again only
        // doubles the wait before the user is told.
        var primary = new Scripted("", Rejected.With(status));
        var fallback = new Scripted("from the fallback");

        await Assert.ThrowsAsync<ClientResultException>(() => Ask(new FallbackChatClient(primary, fallback)));

        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task Surfaces_the_refusal_when_both_allowances_are_spent()
    {
        var primary = new Scripted("", Rejected.With(429));
        var fallback = new Scripted("", Rejected.With(429));

        var failure = await Assert.ThrowsAsync<ClientResultException>(
            () => Ask(new FallbackChatClient(primary, fallback)));

        Assert.Equal(429, failure.Status);
        Assert.Equal(1, fallback.Calls);
    }
}
