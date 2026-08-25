using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoomBooking.Core.Bookings;
using RoomBooking.Core.Data;

namespace RoomBooking.Tests;

/// <summary>
/// Exercises the service against a real SQLite database rather than a fake, so the seed, the
/// schema and the overlap query are all covered by the same tests as the rules.
/// </summary>
public sealed class BookingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BookingDbContext _db;
    private readonly BookingService _service;

    public BookingServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new BookingDbContext(options);
        _db.Database.EnsureCreated();
        _service = new BookingService(new TestDbContextFactory(options));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static DateTime At(int hour, int minute = 0) => new(2026, 9, 1, hour, minute, 0);

    private Task<BookingResult> Book(
        string room, int fromHour, int toHour, int attendees = 2, string user = "user1") =>
        _service.CreateBookingAsync(room, At(fromHour), At(toHour), "Interview with John Doe", attendees, user);

    [Fact]
    public void Seeds_the_five_rooms_with_their_capacities()
    {
        var rooms = _db.Rooms.OrderBy(r => r.Id).ToDictionary(r => r.Id, r => r.Capacity);

        Assert.Equal(new Dictionary<string, int>
        {
            ["A"] = 4, ["B"] = 6, ["C"] = 8, ["D"] = 12, ["E"] = 20,
        }, rooms);
    }

    [Fact]
    public void Seeds_both_users()
    {
        Assert.Equal(["User1", "User2"], _db.Users.OrderBy(u => u.Username).Select(u => u.Username).ToArray());
    }

    [Fact]
    public async Task Stores_a_valid_booking()
    {
        var result = await Book("C", 10, 11);

        Assert.True(result.Succeeded);
        Assert.Single(_db.Bookings);
        Assert.Equal("C", result.Booking!.RoomId);
    }

    [Fact]
    public async Task Refuses_a_second_booking_over_the_same_slots()
    {
        Assert.True((await Book("C", 10, 12)).Succeeded);

        var clash = await Book("C", 11, 13, user: "user2");

        Assert.Contains(BookingError.OverlapsExistingBooking, clash.Errors);
        Assert.Single(_db.Bookings);
    }

    [Fact]
    public async Task Allows_a_booking_that_starts_when_the_previous_one_ends()
    {
        Assert.True((await Book("C", 10, 12)).Succeeded);
        Assert.True((await Book("C", 12, 13, user: "user2")).Succeeded);
        Assert.Equal(2, _db.Bookings.Count());
    }

    [Fact]
    public async Task Keeps_rooms_independent()
    {
        Assert.True((await Book("C", 10, 12)).Succeeded);
        Assert.True((await Book("D", 10, 12, user: "user2")).Succeeded);
    }

    [Fact]
    public async Task Enforces_the_capacity_of_the_specific_room()
    {
        // Room A holds 4, room E holds 20. The same request succeeds in one and fails in the other.
        Assert.Contains(BookingError.ExceedsRoomCapacity, (await Book("A", 10, 11, attendees: 5)).Errors);
        Assert.True((await Book("E", 10, 11, attendees: 5)).Succeeded);
    }

    [Fact]
    public async Task Rejects_an_unknown_room()
    {
        Assert.Contains(BookingError.RoomNotFound, (await Book("Z", 10, 11)).Errors);
    }

    [Fact]
    public async Task Lists_only_rooms_free_across_the_whole_range()
    {
        await Book("C", 10, 12);

        var availability = await _service.ListAvailableRoomsAsync(At(11), At(13));

        Assert.False(availability.Single(r => r.RoomId == "C").IsFree);
        Assert.True(availability.Single(r => r.RoomId == "D").IsFree);
    }

    [Fact]
    public async Task Filters_available_rooms_by_capacity()
    {
        var availability = await _service.ListAvailableRoomsAsync(At(10), At(11), minimumCapacity: 10);

        Assert.Equal(["D", "E"], availability.Select(r => r.RoomId).ToArray());
    }

    [Fact]
    public async Task Marks_occupied_slots_in_the_schedule()
    {
        await Book("B", 10, 11);

        var schedule = await _service.GetRoomScheduleAsync("B", At(9, 30), At(11, 30));

        Assert.NotNull(schedule);
        Assert.Equal(6, schedule.Capacity);
        Assert.Equal(
            [true, false, false, true],
            schedule.Slots.Select(s => s.IsAvailable).ToArray());
        Assert.Equal("Interview with John Doe", schedule.Slots[1].Title);
    }

    [Fact]
    public async Task Widens_an_unaligned_schedule_window_instead_of_failing()
    {
        var schedule = await _service.GetRoomScheduleAsync("B", At(9, 45), At(10, 15));

        Assert.NotNull(schedule);
        Assert.Equal([At(9, 30), At(10)], schedule.Slots.Select(s => s.Start).ToArray());
    }

    [Fact]
    public async Task Returns_no_schedule_for_an_unknown_room()
    {
        Assert.Null(await _service.GetRoomScheduleAsync("Z", At(9), At(10)));
    }

    [Fact]
    public async Task Cancels_a_booking_the_user_owns()
    {
        var booking = (await Book("C", 10, 11, user: "user1")).Booking!;

        Assert.True((await _service.CancelBookingAsync(booking.Id, "user1")).Succeeded);
        Assert.Empty(_db.Bookings);
    }

    [Fact]
    public async Task Refuses_to_cancel_a_booking_owned_by_someone_else()
    {
        var booking = (await Book("C", 10, 11, user: "user1")).Booking!;

        var result = await _service.CancelBookingAsync(booking.Id, "user2");

        Assert.Equal(CancelError.NotOwnedByUser, result.Error);
        Assert.Single(_db.Bookings);
    }

    [Fact]
    public async Task Reports_a_missing_booking_on_cancel()
    {
        var result = await _service.CancelBookingAsync(Guid.NewGuid(), "user1");
        Assert.Equal(CancelError.BookingNotFound, result.Error);
    }

    [Fact]
    public async Task Lists_only_the_requesting_users_bookings()
    {
        await Book("C", 10, 11, user: "user1");
        await Book("D", 10, 11, user: "user2");

        var mine = await _service.ListUserBookingsAsync("user1");

        Assert.Equal("C", Assert.Single(mine).RoomId);
    }
}
