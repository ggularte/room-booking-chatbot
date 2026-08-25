namespace RoomBooking.Tests;

/// <summary>
/// A clock stopped at a chosen moment, which the test can move. Tests involving "now" need the
/// present to be a value they control, or they pass and fail depending on the day they are run.
/// </summary>
public sealed class FixedClock(DateTime now) : TimeProvider
{
    public DateTime Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
