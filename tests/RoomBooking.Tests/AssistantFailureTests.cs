using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using RoomBooking.Agent;

namespace RoomBooking.Tests;

/// <summary>
/// How provider failures reach the user. The provider's own wording is about organisations, service
/// tiers and token budgets; none of that helps someone trying to book a room, and a failure left
/// untranslated shows up in the chat window as a bubble that never resolves.
///
/// The failures are faked rather than provoked: reproducing a spent daily allowance means spending
/// one, and the translation is what these are about.
/// </summary>
public class AssistantFailureTests
{
    private sealed class NoUser : IUserContext { public string UserId => "user1"; }

    private sealed class FailingChatClient(Exception failure) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default) =>
            throw failure;

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public object? GetService(Type t, object? key = null) => null;
        public void Dispose() { }
    }

    private static async Task<AssistantUnavailableException> Failure(Exception thrown)
    {
        var clock = new FixedClock(new DateTime(2026, 9, 1, 9, 0, 0));
        var assistant = new BookingAssistant(
            new FailingChatClient(thrown),
            new BookingTools(TestDatabase.NewService(clock), new NoUser()),
            clock);

        return await Assert.ThrowsAsync<AssistantUnavailableException>(
            () => assistant.ContinueAsync([new ChatMessage(ChatRole.User, "hello")], "User1"));
    }

    private static ClientResultException Rejected(int status) => new(new StubResponse(status));

    [Fact]
    public async Task A_spent_allowance_is_explained_without_jargon()
    {
        var failure = await Failure(Rejected(429));

        Assert.Contains("allowance", failure.Message, StringComparison.OrdinalIgnoreCase);

        // The booking system does not depend on the model, and the reader should know that.
        Assert.Contains("booking system itself is unaffected", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("429", failure.Message);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Other_refusals_are_reported_as_such(int status)
    {
        var failure = await Failure(Rejected(status));
        Assert.Contains("refused the request", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_timeout_is_reported_rather_than_left_pending()
    {
        var failure = await Failure(new TaskCanceledException("timed out"));
        Assert.Contains("took too long", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_original_failure_is_kept_for_the_logs()
    {
        var thrown = Rejected(429);
        var failure = await Failure(thrown);
        Assert.Same(thrown, failure.InnerException);
    }

    private sealed class StubResponse(int status) : PipelineResponse
    {
        public override int Status => status;
        public override string ReasonPhrase => "stubbed";
        public override Stream? ContentStream { get; set; }
        public override BinaryData Content => BinaryData.FromString("");
        protected override PipelineResponseHeaders HeadersCore { get; } = new StubHeaders();
        public override BinaryData BufferContent(CancellationToken ct = default) => Content;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(Content);
        public override void Dispose() { }

        private sealed class StubHeaders : PipelineResponseHeaders
        {
            public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
                Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
            public override bool TryGetValue(string name, out string? value) { value = null; return false; }
            public override bool TryGetValues(string name, out IEnumerable<string>? values) { values = null; return false; }
        }
    }
}
