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
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);

        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!IsAllowed(link.Url))
            {
                // Kept as text rather than removed: the reader should still see what was written.
                link.Url = null;
                link.IsImage = false;
            }
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);

        return new MarkupString(writer.ToString());
    }

    private static bool IsAllowed(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var parsed))
            return false;

        // Relative targets carry no scheme and cannot leave the origin. This has to be asked before
        // the scheme is read: on Unix, parsing "/rooms" as absolute succeeds and yields file://.
        if (!parsed.IsAbsoluteUri)
            return true;

        return AllowedSchemes.Contains(parsed.Scheme, StringComparer.OrdinalIgnoreCase);
    }
}
