namespace RoomBooking.Tests;

/// <summary>Whether the live provider can be reached, and whether its absence is tolerable.</summary>
public static class LiveModel
{
    public static string? ApiKey => Environment.GetEnvironmentVariable("GROQ_API_KEY");

    /// <summary>
    /// Skipping quietly is a false green: a pipeline that lost its key would report success while
    /// never testing the model. REQUIRE_LIVE_TESTS=1 turns the absent key into a failure instead.
    /// </summary>
    public static bool Available
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ApiKey))
                return true;

            Assert.False(
                Environment.GetEnvironmentVariable("REQUIRE_LIVE_TESTS") == "1",
                "REQUIRE_LIVE_TESTS=1 but GROQ_API_KEY is not set, so the live tests could not run.");

            return false;
        }
    }
}
