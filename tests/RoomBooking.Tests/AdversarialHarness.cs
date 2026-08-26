using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using RoomBooking.Agent;
using RoomBooking.Core.Bookings;
using RoomBooking.Core.Data;

namespace RoomBooking.Tests;

/// <summary>
/// Drives the assistant against the live model with a fresh database per case, and reports what the
/// tools did and what survived in storage. The reply text is not evidence; the rows are.
/// </summary>
public sealed class AdversarialHarness : IDisposable
{
    public const string Model = "openai/gpt-oss-120b";
    public static readonly DateTime Now = new(2026, 9, 1, 9, 0, 0);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<BookingDbContext> _options;

    public BookingService Service { get; }

    private sealed class Signed(string id) : IUserContext
    {
        public string UserId { get; } = id;
    }

    public AdversarialHarness()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<BookingDbContext>().UseSqlite(_connection).Options;
        using (var db = new BookingDbContext(_options))
            db.Database.EnsureCreated();

        Service = new BookingService(new TestDbContextFactory(_options), new FixedClock(Now));
    }

    public BookingDbContext Read() => new(_options);

    public BookingAssistant AssistantFor(string userId, string apiKey)
    {
        var chat = GroqChatClient.Create(apiKey, Model, new Uri("https://api.groq.com/openai/v1"));

        return new BookingAssistant(chat, new BookingTools(Service, new Signed(userId)), new FixedClock(Now));
    }

    public static string[] ToolsCalled(IEnumerable<ChatMessage> history) =>
        history.SelectMany(m => m.Contents.OfType<FunctionCallContent>()).Select(c => c.Name).ToArray();

    public void Dispose() => _connection.Dispose();
}
