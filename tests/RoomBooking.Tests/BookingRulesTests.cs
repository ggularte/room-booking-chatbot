using RoomBooking.Core.Bookings;
using RoomBooking.Core.Domain;

namespace RoomBooking.Tests;

/// <summary>
/// The constraints from the challenge, pinned as tests. These exist to prove the rules hold in
/// code rather than in the system prompt — the assistant can only report what these allow.
/// </summary>
public class BookingRulesTests
{
    private static readonly Room RoomA = new() { Id = "A", Capacity = 6 };

    /// <summary>The present, for these tests. Every fixture time below sits after it.</summary>
    private static readonly DateTime Now = new(2026, 9, 1, 8, 0, 0);

    private static DateTime At(int hour, int minute = 0) => new(2026, 9, 1, hour, minute, 0);

    private static Booking Existing(string roomId, DateTime start, DateTime end) => new()
    {
        RoomId = roomId,
        UserId = "user1",
        Title = "Existing",
        Start = start,
        End = end,
        Attendees = 2,
    };

    private static IReadOnlyList<BookingError> Validate(
        DateTime start, DateTime end, int attendees = 2, string? title = "Interview with John Doe",
        Room? room = null, params Booking[] existing) =>
        BookingRules.Validate(room ?? RoomA, title, start, end, attendees, existing, Now);

    [Fact]
    public void Accepts_a_one_hour_booking_within_capacity()
    {
        Assert.Empty(Validate(At(10), At(11)));
    }

    [Fact]
    public void Accepts_a_booking_of_exactly_three_hours()
    {
        Assert.Empty(Validate(At(10), At(13)));
    }

    [Fact]
    public void Rejects_a_booking_longer_than_three_hours()
    {
        Assert.Contains(BookingError.ExceedsMaxDuration, Validate(At(10), At(13, 30)));
    }

    [Fact]
    public void Rejects_an_end_that_does_not_follow_the_start()
    {
        Assert.Contains(BookingError.EndNotAfterStart, Validate(At(11), At(10)));
    }

    [Theory]
    [InlineData(10, 15)]
    [InlineData(10, 45)]
    [InlineData(10, 1)]
    public void Rejects_boundaries_off_the_thirty_minute_grid(int hour, int minute)
    {
        Assert.Contains(BookingError.NotAlignedToSlot, Validate(new DateTime(2026, 9, 1, hour, minute, 0), At(12)));
    }

    [Fact]
    public void Rejects_more_attendees_than_the_room_holds()
    {
        Assert.Contains(BookingError.ExceedsRoomCapacity, Validate(At(10), At(11), attendees: RoomA.Capacity + 1));
    }

    [Fact]
    public void Accepts_attendees_exactly_at_capacity()
    {
        Assert.Empty(Validate(At(10), At(11), attendees: RoomA.Capacity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Rejects_a_non_positive_attendee_count(int attendees)
    {
        Assert.Contains(BookingError.AttendeesMustBePositive, Validate(At(10), At(11), attendees));
    }

    [Fact]
    public void Rejects_a_missing_title()
    {
        Assert.Contains(BookingError.TitleRequired, Validate(At(10), At(11), title: "   "));
    }

    [Fact]
    public void Rejects_an_unknown_room()
    {
        Assert.Contains(BookingError.RoomNotFound, BookingRules.Validate(
            room: null, "Title", At(10), At(11), 2, [], Now));
    }

    // The example the challenge spells out: an appointment running 10:00-11:30 blocks any
    // start before 11:30, and leaves 11:30 onwards free.

    [Theory]
    [InlineData(10, 0)]
    [InlineData(10, 30)]
    [InlineData(11, 0)]
    public void Rejects_a_start_before_an_existing_booking_ends(int hour, int minute)
    {
        var errors = Validate(
            new DateTime(2026, 9, 1, hour, minute, 0), At(12), 2, "Standup", RoomA,
            Existing("A", At(10), At(11, 30)));

        Assert.Contains(BookingError.OverlapsExistingBooking, errors);
    }

    [Fact]
    public void Accepts_a_booking_starting_exactly_when_the_previous_one_ends()
    {
        var errors = Validate(At(11, 30), At(12), 2, "Standup", RoomA, Existing("A", At(10), At(11, 30)));
        Assert.Empty(errors);
    }

    [Fact]
    public void Rejects_a_booking_that_swallows_an_existing_one()
    {
        var errors = Validate(At(9), At(12), 2, "Offsite", RoomA, Existing("A", At(10), At(11)));
        Assert.Contains(BookingError.OverlapsExistingBooking, errors);
    }

    [Fact]
    public void Reports_every_broken_constraint_at_once()
    {
        var errors = Validate(At(10, 15), At(14, 15), attendees: 99, title: "");

        Assert.Contains(BookingError.TitleRequired, errors);
        Assert.Contains(BookingError.NotAlignedToSlot, errors);
        Assert.Contains(BookingError.ExceedsMaxDuration, errors);
        Assert.Contains(BookingError.ExceedsRoomCapacity, errors);
    }

    // Only finished bookings are refused. A meeting already under way is still worth recording.

    [Fact]
    public void Rejects_a_booking_that_has_already_ended()
    {
        var errors = Validate(At(6), At(7));
        Assert.Contains(BookingError.EndsInThePast, errors);
    }

    [Fact]
    public void Rejects_a_booking_ending_exactly_now()
    {
        var errors = Validate(At(7), At(8));
        Assert.Contains(BookingError.EndsInThePast, errors);
    }

    [Fact]
    public void Accepts_a_booking_already_under_way()
    {
        // Started at 07:30, it is now 08:00, and it runs until 09:00.
        var errors = Validate(At(7, 30), At(9));
        Assert.Empty(errors);
    }

    [Fact]
    public void Accepts_a_booking_in_the_future()
    {
        Assert.Empty(Validate(At(10), At(11)));
    }

    [Fact]
    public void Enumerates_the_slots_a_range_covers()
    {
        var slots = BookingRules.SlotsIn(At(10), At(11, 30)).ToList();
        Assert.Equal([At(10), At(10, 30), At(11)], slots);
    }
}
