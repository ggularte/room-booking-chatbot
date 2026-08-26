namespace RoomBooking.Agent;

/// <summary>
/// The model could not be reached, or refused to answer. Carries a sentence fit to show a user:
/// the provider's own wording is about organisations, tiers and token budgets, none of which mean
/// anything to someone trying to book a room.
/// </summary>
public sealed class AssistantUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
