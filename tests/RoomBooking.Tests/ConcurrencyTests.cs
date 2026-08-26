using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoomBooking.Core.Bookings;
using RoomBooking.Core.Data;

namespace RoomBooking.Tests;

/// <summary>
/// Two users going for the same slot. Run against a file-backed database, because in-memory SQLite
/// does not lock the way the deployed one does.
///
/// The interleaving that actually breaks the rule — both readers seeing a free slot before either
/// writes — cannot be produced by firing requests in parallel and hoping: SQLite serialises them on
/// its own, so such a test passes with or without the guard and proves nothing. These hold the lock
/// explicitly instead.
/// </summary>
public sealed class ConcurrencyTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rb-{Guid.NewGuid():N}.db");
    private readonly string _connectionString;
    private readonly DbContextOptions<BookingDbContext> _options;
    private readonly BookingService _service;

    private static readonly DateTime Slot = new(2026, 9, 2, 10, 0, 0);

    public ConcurrencyTests()
    {
        // One second, so a blocked request gives up while the test is still young.
        _connectionString = $"Data Source={_path};Default Timeout=1";

        _options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseSqlite(_connectionString)
            .Options;

        using (var db = new BookingDbContext(_options))
            db.Database.EnsureCreated();

        _service = new BookingService(
            new TestDbContextFactory(_options), new FixedClock(new DateTime(2026, 9, 1, 9, 0, 0)));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }

    private Task<BookingResult> Book(string user, DateTime start, DateTime end) =>
        _service.CreateBookingAsync("C", start, end, $"{user} meeting", 2, user);

    [Fact]
    public async Task Refuses_cleanly_when_the_database_is_locked_by_someone_else()
    {
        // Somebody else's write transaction, still open. Before this was handled, the request waited
        // thirty seconds and then threw, and the user was told the assistant was unreachable.
        await using var holder = new SqliteConnection(_connectionString);
        await holder.OpenAsync();
        await using var lockHeld = holder.BeginTransaction();
        await using (var write = holder.CreateCommand())
        {
            write.Transaction = lockHeld;
            write.CommandText = "UPDATE Rooms SET Capacity = Capacity WHERE Id = 'C'";
            await write.ExecuteNonQueryAsync();
        }

        var result = await Book("user1", Slot, Slot.AddHours(1));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, problem => problem.Error == BookingError.CouldNotSecureTheSlot);
    }

    [Fact]
    public async Task Recovers_once_the_lock_is_released()
    {
        await using (var holder = new SqliteConnection(_connectionString))
        {
            await holder.OpenAsync();
            await using var lockHeld = holder.BeginTransaction();
            await using (var write = holder.CreateCommand())
            {
                write.Transaction = lockHeld;
                write.CommandText = "UPDATE Rooms SET Capacity = Capacity WHERE Id = 'C'";
                await write.ExecuteNonQueryAsync();
            }

            Assert.False((await Book("user1", Slot, Slot.AddHours(1))).Succeeded);
            await lockHeld.RollbackAsync();
        }

        Assert.True((await Book("user1", Slot, Slot.AddHours(1))).Succeeded);
    }

    [Fact]
    public async Task Stores_one_booking_when_several_requests_target_the_same_slot()
    {
        var attempts = Enumerable.Range(0, 8)
            .Select(i => Book(i % 2 == 0 ? "user1" : "user2", Slot, Slot.AddMinutes(30)));

        var results = await Task.WhenAll(attempts);

        using var db = new BookingDbContext(_options);
        Assert.Single(db.Bookings);
        Assert.Equal(1, results.Count(r => r.Succeeded));
    }

    [Fact]
    public async Task Neighbouring_slots_do_not_block_each_other()
    {
        var attempts = Enumerable.Range(0, 6)
            .Select(i => Book("user1", Slot.AddMinutes(30 * i), Slot.AddMinutes(30 * (i + 1))));

        var results = await Task.WhenAll(attempts);

        using var db = new BookingDbContext(_options);
        Assert.Equal(6, db.Bookings.Count());
        Assert.All(results, r => Assert.True(r.Succeeded));
    }
}
