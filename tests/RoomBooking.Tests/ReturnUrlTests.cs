using RoomBooking.Web.Auth;

namespace RoomBooking.Tests;

/// <summary>
/// The sign-in page follows a destination taken from the query string. Anyone can write a query
/// string, so this decides what is followed and what is discarded.
/// </summary>
public class ReturnUrlTests
{
    [Theory]
    [InlineData("/rooms")]
    [InlineData("/rooms?date=2026-09-02")]
    [InlineData("rooms")]
    public void Follows_a_path_inside_the_app(string target)
    {
        Assert.Equal(target, ReturnUrl.Safe(target));
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://evil.example/steal")]
    [InlineData("javascript:alert(1)")]
    [InlineData("//evil.example")]
    [InlineData("///evil.example")]
    [InlineData("\\\\evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("\\/evil.example")]
    [InlineData("  //evil.example  ")]
    public void Discards_anything_that_leaves_the_origin(string target)
    {
        Assert.Equal(ReturnUrl.Fallback, ReturnUrl.Safe(target));
    }

    [Fact]
    public void Discards_a_fragment_rather_than_stripping_it()
    {
        // A fragment is not a well-formed relative reference, so this falls back. Nothing is lost:
        // browsers never send the fragment to the server, so one cannot arrive here anyway.
        Assert.Equal(ReturnUrl.Fallback, ReturnUrl.Safe("/rooms#today"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Falls_back_when_nothing_was_asked_for(string? target)
    {
        Assert.Equal(ReturnUrl.Fallback, ReturnUrl.Safe(target));
    }
}
