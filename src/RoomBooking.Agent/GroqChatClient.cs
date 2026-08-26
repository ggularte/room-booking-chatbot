using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;

namespace RoomBooking.Agent;

/// <summary>
/// Builds the chat client. Groq speaks the OpenAI wire format, so the OpenAI client works unchanged
/// once its endpoint is repointed; no Groq-specific SDK is involved and switching provider is a
/// configuration change.
///
/// One place rather than two, so tests exercise the settings the deployed application runs with.
/// </summary>
public static class GroqChatClient
{
    public static IChatClient Create(string apiKey, string model, Uri endpoint)
    {
        var openAi = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = endpoint,

                // No retries, deliberately. A rejected request comes back with a Retry-After that
                // the default policy honours by sleeping — for a spent daily allowance that is
                // nearly five minutes, during which the chat window shows a pending bubble and
                // nothing else. Waiting cannot refill an exhausted quota, and someone told what
                // happened can decide for themselves whether to wait.
                NetworkTimeout = TimeSpan.FromSeconds(45),
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            });

        return new ChatClientBuilder(openAi.GetChatClient(model).AsIChatClient())
            .UseFunctionInvocation()
            .Build();
    }
}
