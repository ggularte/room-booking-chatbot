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
        Assert.Contains(result.Problems, p => p.Contains("holds only", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Availability_names_the_one_room_that_fits_and_why_the_rest_do_not()
    {
        var availability = await Tools().ListAvailableRoomsAsync(
            "2026-09-02T15:00:00", "2026-09-02T15:30:00", minimumCapacity: 13);

        // Only E holds thirteen. Deciding that is arithmetic, and it is done here rather than left
        // to the model, which read a list of flags as "nothing is available".
        Assert.Equal(["E"], availability.Suitable.Select(r => r.RoomId).ToArray());
        Assert.Equal(["A", "B", "C", "D"], availability.Unsuitable.Select(r => r.RoomId).ToArray());
        Assert.All(availability.Unsuitable, r => Assert.Contains("holds only", r.Reason!));
    }

    [Fact]
    public async Task Availability_separates_being_too_small_from_being_taken()
    {
        var tools = Tools();
        await tools.CreateBookingAsync("E", "2026-09-02T15:00:00", "2026-09-02T16:00:00", "Taken", 2);

        var availability = await tools.ListAvailableRoomsAsync(
            "2026-09-02T15:00:00", "2026-09-02T15:30:00", minimumCapacity: 13);

        // One is solved by moving the meeting, the other by shrinking it.
        Assert.Empty(availability.Suitable);
        Assert.Contains("already booked", availability.Unsuitable.Single(r => r.RoomId == "E").Reason!);
        Assert.Contains("holds only 4", availability.Unsuitable.Single(r => r.RoomId == "A").Reason!);
    }

    [Fact]
    public async Task Availability_says_when_a_room_is_taken_not_merely_that_it_is()
    {
        // "Busy for part of that range" leaves the asker where they started. The hours let them
        // pick another time without asking a second question.
        var tools = Tools();
        await tools.CreateBookingAsync("C", "2026-09-02T09:00:00", "2026-09-02T12:00:00", "Workshop", 4);

        var availability = await tools.ListAvailableRoomsAsync(
            "2026-09-02T10:00:00", "2026-09-02T11:00:00");

        var c = availability.Unsuitable.Single(r => r.RoomId == "C");
        Assert.Equal("already booked 09:00-12:00", c.Reason);
    }

    [Fact]
    public async Task Availability_lists_every_stretch_a_room_is_taken_for()
    {
        var tools = Tools();
        await tools.CreateBookingAsync("C", "2026-09-02T09:00:00", "2026-09-02T10:00:00", "First", 4);
        await tools.CreateBookingAsync("C", "2026-09-02T11:00:00", "2026-09-02T12:00:00", "Second", 4);

        var availability = await tools.ListAvailableRoomsAsync(
            "2026-09-02T09:00:00", "2026-09-02T12:00:00");

        var c = availability.Unsuitable.Single(r => r.RoomId == "C");
        Assert.Equal("already booked 09:00-10:00 and 11:00-12:00", c.Reason);
    }

    [Fact]
    public async Task Availability_marks_nothing_unsuitable_when_no_group_size_is_given()
    {
        var availability = await Tools().ListAvailableRoomsAsync(
            "2026-09-02T15:00:00", "2026-09-02T15:30:00");

        Assert.Equal(5, availability.Suitable.Length);
        Assert.Empty(availability.Unsuitable);
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

        Assert.Empty(result.Suitable);
        Assert.Empty(result.Unsuitable);
        Assert.Contains(result.Problems, p => p.Contains("not a real date", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task An_availability_search_that_ran_carries_no_problems()
    {
        var result = await Tools().ListAvailableRoomsAsync("2026-09-02T10:00:00", "2026-09-02T11:00:00");

        Assert.NotEmpty(result.Suitable);
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

    // A reason the reader cannot act on is barely better than no reason.

    [Fact]
    public async Task A_refusal_for_capacity_says_how_many_the_room_holds()
    {
        var result = await Tools().CreateBookingAsync(
            "A", "2026-09-02T10:00:00", "2026-09-02T11:00:00", "All hands", 30);

        Assert.False(result.Success);
        Assert.Contains("holds only 4", Assert.Single(result.Problems));
    }

    [Fact]
    public async Task A_refusal_for_an_overlap_says_when_the_room_is_taken()
    {
        var tools = Tools();
        await tools.CreateBookingAsync("C", "2026-09-02T09:00:00", "2026-09-02T12:00:00", "Workshop", 4);

        var clash = await tools.CreateBookingAsync(
            "C", "2026-09-02T10:00:00", "2026-09-02T11:00:00", "Sync", 4);

        Assert.False(clash.Success);
        Assert.Contains("already booked 09:00-12:00", Assert.Single(clash.Problems));
    }

    [Fact]
    public async Task A_refusal_for_an_overlap_lists_every_booking_in_the_way()
    {
        var tools = Tools();
        await tools.CreateBookingAsync("C", "2026-09-02T09:00:00", "2026-09-02T10:00:00", "First", 4);
        await tools.CreateBookingAsync("C", "2026-09-02T11:00:00", "2026-09-02T12:00:00", "Second", 4);

        var clash = await tools.CreateBookingAsync(
            "C", "2026-09-02T09:00:00", "2026-09-02T12:00:00", "Offsite", 4);

        Assert.Contains("already booked 09:00-10:00 and 11:00-12:00", Assert.Single(clash.Problems));
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
