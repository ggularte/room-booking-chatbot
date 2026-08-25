namespace RoomBooking.Tests;

/// <summary>
/// A clock stopped at a chosen moment. Tests that involve "the past" need the present to be a fixed
/// value, or they start passing and failing depending on the day they are run.
/// </summary>
public sealed class FixedClock(DateTime now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
