using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace RoomBooking.Agent;

/// <summary>
/// Falls back to a second model when the first has spent its allowance.
///
/// Groq's free tier allows a fixed number of tokens per day <em>per model</em>, so a day of use
/// exhausts one while leaving the others untouched. Without this, the assistant stops working for
/// everyone until the allowance rolls over — including whoever opens it next.
///
/// It wraps the bare model clients, beneath the tool-invocation loop rather than around it. Wrapped
/// around, a refusal arriving after a booking had already been created would restart the whole turn
/// on the other model and could create it a second time. Beneath, the loop keeps its history and
/// its tool results, and only the call that was refused is repeated.
/// </summary>
public sealed class FallbackChatClient(
    IChatClient primary,
    IChatClient fallback,
    ILogger<FallbackChatClient>? logger = null) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
    {
        try
        {
            return await primary.GetResponseAsync(messages, options, ct);
        }
        catch (ClientResultException refused) when (refused.Status == 429)
        {
            logger?.LogWarning(
                "The primary model refused the request as over its allowance; falling back.");

            return await fallback.GetResponseAsync(messages, options, ct);
        }
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default) =>
        throw new NotSupportedException("The assistant does not stream.");

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        primary.GetService(serviceType, serviceKey) ?? fallback.GetService(serviceType, serviceKey);

    public void Dispose()
    {
        primary.Dispose();
        fallback.Dispose();
    }
}
