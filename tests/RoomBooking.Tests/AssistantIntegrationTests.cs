using System.ClientModel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using RoomBooking.Agent;
using RoomBooking.Core.Bookings;
using RoomBooking.Core.Data;

namespace RoomBooking.Tests;

/// <summary>
/// Exercises the whole path — prompt, model, tool call, validation, database — against the real
/// provider. Skipped when GROQ_API_KEY is absent so the suite still runs offline and in CI.
/// </summary>
public sealed class AssistantIntegrationTests : IDisposable
{
    private const string Model = "openai/gpt-oss-120b";

    private readonly SqliteConnection _connection;
    private readonly BookingDbContext _db;
    private readonly BookingService _service;
    private readonly string? _apiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");

    private sealed class FixedUser(string id) : IUserContext
    {
        public string UserId { get; } = id;
    }

    public AssistantIntegrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new BookingDbContext(new DbContextOptionsBuilder<BookingDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _service = new BookingService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private BookingAssistant Assistant(DateTime now)
    {
        var openAi = new OpenAIClient(
            new ApiKeyCredential(_apiKey!),
            new OpenAIClientOptions { Endpoint = new Uri("https://api.groq.com/openai/v1") });

        var chat = new ChatClientBuilder(openAi.GetChatClient(Model).AsIChatClient())
            .UseFunctionInvocation()
            .Build();

        var clock = new FakeTimeProvider(now);
        return new BookingAssistant(chat, new BookingTools(_service, new FixedUser("user1")), clock);
    }

    private sealed class FakeTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public async Task Books_a_room_from_a_plain_language_request()
    {
        if (_apiKey is null) return;

        var history = new List<ChatMessage>
        {
            new(ChatRole.User,
                "Book room C tomorrow from 10:00 to 11:30 for 4 people. Title it 'Interview with John Doe'."),
        };

        await Assistant(new DateTime(2026, 9, 1, 9, 0, 0)).ContinueAsync(history, "User1");

        var booking = Assert.Single(_db.Bookings);
        Assert.Equal("C", booking.RoomId);
        Assert.Equal("user1", booking.UserId);
        Assert.Equal(4, booking.Attendees);
        Assert.Equal(new DateTime(2026, 9, 2, 10, 0, 0), booking.Start);
        Assert.Equal(new DateTime(2026, 9, 2, 11, 30, 0), booking.End);
    }

    [Fact]
    public async Task Refuses_a_booking_that_breaks_a_rule_and_stores_nothing()
    {
        if (_apiKey is null) return;

        var history = new List<ChatMessage>
        {
            new(ChatRole.User,
                "Book room A tomorrow from 10:00 to 15:00 for 30 people, title 'All hands'."),
        };

        var response = await Assistant(new DateTime(2026, 9, 1, 9, 0, 0)).ContinueAsync(history, "User1");

        // Room A holds 4 and the range is 5 hours: the tool must refuse, and the assistant must not
        // claim otherwise or silently book something smaller.
        Assert.Empty(_db.Bookings);
        Assert.False(string.IsNullOrWhiteSpace(response.Text));
    }
}
