using RoomBooking.Agent;

namespace RoomBooking.Tests;

/// <summary>
/// What the tools hand back to the model. No network involved: these are about the shape of the
/// data, which is where the model's mistakes can be prevented rather than corrected.
/// </summary>
public class ToolResponseTests
{
    private sealed class Signed(string id) : IUserContext
    {
        public string UserId { get; } = id;
    }

    private static readonly DateTime Now = new(2026, 9, 1, 9, 0, 0);

    private static BookingTools Tools(DateTime? now = null) =>
        new(TestDatabase.NewService(new FixedClock(now ?? Now)), new Signed("user1"));

    [Fact]
    public async Task A_confirmed_booking_names_the_weekday()
    {
        // Left to work out which day "tomorrow" lands on, the model gets it wrong often enough to
        // announce the wrong one. The tool answers that question so it does not have to.
        var tools = Tools();

        var result = await tools.CreateBookingAsync(
            "C", "2026-09-02T10:00:00", "2026-09-02T11:00:00", "Retro", 4);

        Assert.True(result.Success);
        Assert.Contains("Wednesday", result.Start);
        Assert.Contains("2026-09-02", result.Start);
    }

    [Fact]
    public async Task A_confirmed_booking_echoes_what_was_stored()
    {
        var tools = Tools();

        var result = await tools.CreateBookingAsync(
            "C", "2026-09-02T10:00:00", "2026-09-02T11:00:00", "  Retro  ", 4);

        // Trimmed by the service, and reported back as stored rather than as requested.
        Assert.Equal("Retro", result.Title);
        Assert.Equal("C", result.RoomId);
        Assert.Equal(4, result.Attendees);
    }

    [Fact]
    public async Task A_refusal_carries_the_reason_in_words()
    {
        var tools = Tools();

        var result = await tools.CreateBookingAsync(
            "A", "2026-09-02T10:00:00", "2026-09-02T11:00:00", "All hands", 30);

        Assert.False(result.Success);
        Assert.Contains(result.Problems, p => p.Contains("hold that many", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_schedule_hides_other_peoples_titles()
    {
        var service = TestDatabase.NewService(new FixedClock(Now));
        var start = new DateTime(2026, 9, 2, 10, 0, 0);
        await service.CreateBookingAsync("C", start, start.AddHours(1), "User2 private matter", 2, "user2");

        var tools = new BookingTools(service, new Signed("user1"));
        var schedule = await tools.GetRoomScheduleAsync("C", "2026-09-02T10:00:00", "2026-09-02T11:00:00");

        // An hour is two slots.
        Assert.Equal(2, schedule.Slots.Length);
        Assert.All(schedule.Slots, slot =>
        {
            Assert.False(slot.IsAvailable);
            Assert.False(slot.IsMine);
            Assert.Null(slot.Title);
        });
    }

    // A model that writes an impossible date should learn why, not just that something failed.

    [Theory]
    [InlineData("2026-09-34T10:00:00")]
    [InlineData("2026-09-31T10:00:00")]
    [InlineData("2026-09-02T24:67:00")]
    [InlineData("2026-13-02T10:00:00")]
    [InlineData("2027-02-29T10:00:00")]
    [InlineData("tomorrow at ten")]
    [InlineData("")]
    public async Task An_impossible_moment_is_refused_in_words(string start)
    {
        var result = await Tools().CreateBookingAsync("C", start, "2026-09-02T11:00:00", "Retro", 4);

        Assert.False(result.Success);
        Assert.Contains(result.Problems, p => p.Contains("not a real date", StringComparison.OrdinalIgnoreCase));
    }

    // Reading the calendar is where a silent refusal does the most damage: an empty answer reads as
    // "nothing is free" or "no such room", and the assistant passes that on as fact.

    [Theory]
    [InlineData("2026-09-34T10:00:00", "2026-09-02T11:00:00")]
    [InlineData("2026-09-02T10:00:00", "the day after tomorrow")]
    public async Task An_unreadable_range_stops_the_availability_search(string start, string end)
    {
        var result = await Tools().ListAvailableRoomsAsync(start, end);

        Assert.Empty(result.Rooms);
        Assert.Contains(result.Problems, p => p.Contains("not a real date", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task An_availability_search_that_ran_carries_no_problems()
    {
        var result = await Tools().ListAvailableRoomsAsync("2026-09-02T10:00:00", "2026-09-02T11:00:00");

        Assert.NotEmpty(result.Rooms);
        Assert.Empty(result.Problems);
    }

    [Theory]
    [InlineData("2026-09-34T10:00:00", "2026-09-02T11:00:00")]
    [InlineData("2026-09-02T10:00:00", "")]
    public async Task An_unreadable_range_stops_the_schedule_lookup(string from, string to)
    {
        var schedule = await Tools().GetRoomScheduleAsync("C", from, to);

        Assert.Empty(schedule.Slots);
        Assert.Contains(schedule.Problems, p => p.Contains("not a real date", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task An_unknown_room_says_so_rather_than_returning_nothing()
    {
        var schedule = await Tools().GetRoomScheduleAsync("Z", "2026-09-02T10:00:00", "2026-09-02T11:00:00");

        Assert.Empty(schedule.Slots);
        Assert.Contains(schedule.Problems, p => p.Contains("no such room", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_leap_day_is_a_real_date()
    {
        // 2028 is a leap year; 2027 is not, and is covered above. The clock sits near the date so
        // the booking stays inside the horizon rather than tripping a different rule.
        var result = await Tools(new DateTime(2028, 1, 15, 9, 0, 0)).CreateBookingAsync(
            "C", "2028-02-29T10:00:00", "2028-02-29T11:00:00", "Retro", 4);

        Assert.True(result.Success);
        Assert.Contains("2028-02-29", result.Start);
        Assert.Contains("Tuesday", result.Start);
    }

    [Fact]
    public async Task A_trailing_Z_does_not_shift_the_booking()
    {
        // Models emit one even when told not to. Ten o'clock must stay ten o'clock.
        var result = await Tools().CreateBookingAsync(
            "C", "2026-09-02T10:00:00Z", "2026-09-02T11:00:00Z", "Retro", 4);

        Assert.True(result.Success);
        Assert.Contains("10:00", result.Start);
        Assert.Contains("11:00", result.End);
    }

    [Fact]
    public async Task Room_letters_are_accepted_in_any_case()
    {
        var tools = Tools();

        var result = await tools.CreateBookingAsync(
            " c ", "2026-09-02T10:00:00", "2026-09-02T11:00:00", "Retro", 4);

        Assert.True(result.Success);
        Assert.Equal("C", result.RoomId);
    }
}
