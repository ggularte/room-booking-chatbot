using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace RoomBooking.Agent;

/// <summary>
/// Builds the chat client. Groq speaks the OpenAI wire format, so the OpenAI client works unchanged
/// once its endpoint is repointed; no Groq-specific SDK is involved and switching provider is a
/// configuration change.
///
/// One place rather than several, so tests exercise the settings the deployed application runs with.
/// </summary>
public static class GroqChatClient
{
    public static IChatClient Create(
        string apiKey,
        string model,
        Uri endpoint,
        string? fallbackModel = null,
        ILoggerFactory? loggerFactory = null)
    {
        var openAi = new OpenAIClient(new ApiKeyCredential(apiKey), Options(endpoint));

        IChatClient model_ = openAi.GetChatClient(model).AsIChatClient();

        // A fallback naming the same model would buy nothing: the allowance is per model.
        if (!string.IsNullOrWhiteSpace(fallbackModel) &&
            !string.Equals(fallbackModel, model, StringComparison.OrdinalIgnoreCase))
        {
            model_ = new FallbackChatClient(
                model_,
                openAi.GetChatClient(fallbackModel).AsIChatClient(),
                loggerFactory?.CreateLogger<FallbackChatClient>());
        }

        return new ChatClientBuilder(model_)
            .UseFunctionInvocation()
            .Build();
    }

    private static OpenAIClientOptions Options(Uri endpoint) => new()
    {
        Endpoint = endpoint,

        // No retries, deliberately. A rejected request comes back with a Retry-After that the
        // default policy honours by sleeping — for a spent daily allowance that is nearly five
        // minutes, during which the chat window shows a pending bubble and nothing else. Waiting
        // cannot refill an exhausted quota, and the fallback answers in the meantime.
        NetworkTimeout = TimeSpan.FromSeconds(45),
        RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
    };
}
