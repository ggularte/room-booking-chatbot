using RoomBooking.Web;

namespace RoomBooking.Tests;

/// <summary>
/// Assistant replies are Markdown rendered into the page. The text comes from a model that has just
/// read user input and tool output, including booking titles another user chose, so it is hostile
/// input as far as rendering is concerned.
/// </summary>
public class MarkdownRendererTests
{
    private static string Render(string markdown) => MarkdownRenderer.ToHtml(markdown).Value;

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe>")]
    [InlineData("<a href=\"https://evil.example\">click</a>")]
    public void Never_emits_raw_html_as_markup(string hostile)
    {
        var html = Render($"The meeting is called {hostile}.");

        // The words may appear as escaped text; what must not appear is a live element.
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img src=x", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<a href=\"https://evil.example\"", html, StringComparison.OrdinalIgnoreCase);
    }

    // Disabling raw HTML does nothing for the targets Markdown builds itself. These are the ones
    // that actually execute when clicked.

    [Theory]
    [InlineData("[click](javascript:alert(1))")]
    [InlineData("[click](JavaScript:alert(1))")]
    [InlineData("[click](vbscript:msgbox(1))")]
    [InlineData("[click](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)")]
    [InlineData("![img](javascript:alert(1))")]
    public void Drops_link_targets_that_are_not_a_safe_scheme(string hostile)
    {
        var html = Render(hostile);

        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vbscript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", html, StringComparison.OrdinalIgnoreCase);
    }

    // A link needs no scheme of its own to leave the origin: the browser lends it the page's.

    [Theory]
    [InlineData("[click](//evil.example/steal)")]
    // Four backslashes so that CommonMark's own escaping leaves two in the destination.
    [InlineData(@"[click](\\\\evil.example/steal)")]
    [InlineData(@"[click](/\evil.example/steal)")]
    [InlineData("![img](//evil.example/track.png)")]
    public void Drops_link_targets_that_leave_the_origin_without_a_scheme(string hostile)
    {
        Assert.DoesNotContain("evil.example", Render(hostile));
    }

    // Autolinks are a different node from bracket links, and were rendered without being asked.

    [Theory]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("<vbscript:msgbox(1)>")]
    [InlineData("<data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==>")]
    public void Drops_autolinks_that_are_not_a_safe_scheme(string hostile)
    {
        var html = Render(hostile);

        Assert.DoesNotContain("<a ", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<https://example.com>", "https://example.com")]
    [InlineData("<someone@example.com>", "mailto:someone@example.com")]
    public void Keeps_ordinary_autolinks(string markdown, string expected)
    {
        Assert.Contains($"href=\"{expected}\"", Render(markdown));
    }

    [Theory]
    [InlineData("[docs](https://example.com)", "https://example.com")]
    [InlineData("[mail](mailto:someone@example.com)", "mailto:someone@example.com")]
    [InlineData("[here](/rooms)", "/rooms")]
    public void Keeps_ordinary_links(string markdown, string expected)
    {
        Assert.Contains($"href=\"{expected}\"", Render(markdown));
    }

    [Fact]
    public void Renders_ordinary_markdown()
    {
        var html = Render("Room **C** is free:\n\n- 10:00\n- 10:30");

        Assert.Contains("<strong>C</strong>", html);
        Assert.Contains("<li>", html);
    }

    [Fact]
    public void Handles_an_empty_reply()
    {
        Assert.Equal(string.Empty, Render("").Trim());
    }
}
