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

    private static BookingTools Tools() =>
        new(TestDatabase.NewService(new FixedClock(Now)), new Signed("user1"));

    [Fact]
    public async Task A_confirmed_booking_names_the_weekday()
    {
        // Left to work out which day "tomorrow" lands on, the model gets it wrong often enough to
        // announce the wrong one. The tool answers that question so it does not have to.
        var tools = Tools();

        var result = await tools.CreateBookingAsync(
            "C", new DateTime(2026, 9, 2, 10, 0, 0), new DateTime(2026, 9, 2, 11, 0, 0), "Retro", 4);

        Assert.True(result.Success);
        Assert.Contains("Wednesday", result.Start);
        Assert.Contains("2026-09-02", result.Start);
    }

    [Fact]
    public async Task A_confirmed_booking_echoes_what_was_stored()
    {
        var tools = Tools();

        var result = await tools.CreateBookingAsync(
            "C", new DateTime(2026, 9, 2, 10, 0, 0), new DateTime(2026, 9, 2, 11, 0, 0), "  Retro  ", 4);

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
            "A", new DateTime(2026, 9, 2, 10, 0, 0), new DateTime(2026, 9, 2, 11, 0, 0), "All hands", 30);

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
        var schedule = await tools.GetRoomScheduleAsync("C", start, start.AddHours(1));

        // An hour is two slots.
        Assert.Equal(2, schedule!.Slots.Length);
        Assert.All(schedule.Slots, slot =>
        {
            Assert.False(slot.IsAvailable);
            Assert.False(slot.IsMine);
            Assert.Null(slot.Title);
        });
    }

    [Fact]
    public async Task Room_letters_are_accepted_in_any_case()
    {
        var tools = Tools();

        var result = await tools.CreateBookingAsync(
            " c ", new DateTime(2026, 9, 2, 10, 0, 0), new DateTime(2026, 9, 2, 11, 0, 0), "Retro", 4);

        Assert.True(result.Success);
        Assert.Equal("C", result.RoomId);
    }
}
