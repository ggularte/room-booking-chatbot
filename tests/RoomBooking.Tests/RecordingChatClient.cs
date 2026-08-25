using Microsoft.Extensions.AI;

namespace RoomBooking.Tests;

/// <summary>
/// Stands in for the model and keeps whatever it was sent, so tests can assert on the instructions
/// the assistant builds without spending a network call on it.
/// </summary>
public sealed class RecordingChatClient : IChatClient
{
    public List<List<ChatMessage>> Calls { get; } = [];

    public string? LastSystemPrompt => Calls.LastOrDefault()
        ?.FirstOrDefault(m => m.Role == ChatRole.System)?.Text;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
    {
        Calls.Add([.. messages]);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Noted.")));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default) =>
        throw new NotSupportedException("The assistant does not stream.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
