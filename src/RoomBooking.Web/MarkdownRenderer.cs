using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Components;

namespace RoomBooking.Web;

/// <summary>
/// Renders assistant replies, which arrive as Markdown, into HTML for the transcript.
///
/// The text originates from a model that has just consumed user input and tool output — including
/// booking titles another user chose — so it is treated as hostile.
/// </summary>
public static class MarkdownRenderer
{
    // Raw HTML is disabled, so <script> and friends arrive as escaped text.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    /// <summary>
    /// Schemes a link or image may point at. Disabling raw HTML does nothing for the targets
    /// Markdown generates itself: `[click](javascript:...)` yields a real anchor, and clicking it
    /// runs script in the reader's session. Anything not listed here is dropped.
    /// </summary>
    private static readonly string[] AllowedSchemes = ["http", "https", "mailto"];

    public static MarkupString ToHtml(string markdown)
    {
        var document = Markdown.Parse(NormaliseSpacing(markdown ?? string.Empty), Pipeline);

        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!IsAllowed(link.Url))
            {
                // Kept as text rather than removed: the reader should still see what was written.
                link.Url = null;
                link.IsImage = false;
            }
        }

        // Autolinks — `<https://example.com>` — are a separate node type, not a LinkInline, so the
        // loop above never sees them. Markdig renders them as real anchors whatever the scheme,
        // which makes `<javascript:alert(1)>` a live link if it is left alone.
        foreach (var autolink in document.Descendants<AutolinkInline>().ToArray())
        {
            var url = autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url;

            if (!IsAllowed(url))
                autolink.ReplaceBy(new LiteralInline(autolink.Url));
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);

        return new MarkupString(writer.ToString());
    }

    /// <summary>
    /// Replaces the typographic spaces and hyphens the model sprinkles around numbers with their
    /// ordinary equivalents.
    ///
    /// It writes "18:00 to 18:30" using U+202F, a narrow no-break space, and dates using U+2011, a
    /// non-breaking hyphen. Both are legitimate characters and both render narrower than the
    /// ordinary ones, so a sentence carrying a few of them looks unevenly spaced — words appearing
    /// to run together in one phrase and not the next. Accents, curly quotes and en dashes are left
    /// alone: those are correct typography and they render as intended.
    /// </summary>
    private static string NormaliseSpacing(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            switch (character)
            {
                // Spaces narrower than an ordinary one.
                case '\u00A0' or '\u202F' or '\u2007' or '\u2008' or '\u2009' or '\u200A':
                    builder.Append(' ');
                    break;

                // Non-breaking hyphen, as it writes dates.
                case '\u2011':
                    builder.Append('-');
                    break;

                // Invisible joiners, which serve no purpose here.
                case '\u200B' or '\u2060' or '\uFEFF':
                    break;

                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    private static bool IsAllowed(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var parsed))
            return false;

        // A network-path reference — "//example.com/x", and the backslash spellings browsers fold
        // into it — carries no scheme, so Uri reads it as relative. A browser does not: it borrows
        // the page's scheme and navigates off the origin. Refused before the relative check below.
        if (IsNetworkPath(url))
            return false;

        // Relative targets carry no scheme and cannot leave the origin. This has to be asked before
        // the scheme is read: on Unix, parsing "/rooms" as absolute succeeds and yields file://.
        if (!parsed.IsAbsoluteUri)
            return true;

        return AllowedSchemes.Contains(parsed.Scheme, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsNetworkPath(string url)
    {
        var target = url.TrimStart();

        return target.Length > 1
            && target[0] is '/' or '\\'
            && target[1] is '/' or '\\';
    }
}
