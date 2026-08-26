using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace RoomBooking.Agent;

public static class AgentServiceCollectionExtensions
{
    /// <summary>
    /// Registers the assistant against Groq. Groq speaks the OpenAI wire format, so the OpenAI
    /// client works unchanged once its endpoint is repointed — no Groq-specific SDK is involved,
    /// and swapping in OpenAI or any other compatible provider is a configuration change.
    /// </summary>
    public static IServiceCollection AddBookingAssistant(
        this IServiceCollection services,
        string apiKey,
        string model,
        Uri endpoint,
        string? fallbackModel = null)
    {
        services.AddSingleton(provider => GroqChatClient.Create(
            apiKey, model, endpoint, fallbackModel,
            provider.GetService<ILoggerFactory>()));

        services.AddScoped<BookingTools>();
        services.AddScoped<BookingAssistant>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
