using Markdig;
using Microsoft.AspNetCore.Components;

namespace RoomBooking.Web;

/// <summary>
/// Renders assistant replies, which arrive as Markdown, into HTML for the transcript.
/// </summary>
public static class MarkdownRenderer
{
    // Raw HTML is disabled. The text being rendered originates from a language model that has just
    // been fed tool output and user input, so treating it as trusted markup would be an injection
    // route straight into the page.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    public static MarkupString ToHtml(string markdown) =>
        new(Markdown.ToHtml(markdown ?? string.Empty, Pipeline));
}
