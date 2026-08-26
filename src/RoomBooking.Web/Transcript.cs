using Microsoft.Extensions.AI;

namespace RoomBooking.Web;

/// <summary>One line of the conversation as it appears on screen.</summary>
public sealed record TranscriptEntry(bool IsUser, string Text, string[] Tools);

/// <summary>
/// Turns the conversation into what the transcript shows.
/// </summary>
public static class Transcript
{
    /// <summary>
    /// One turn can span several assistant messages — the tool loop emits one per call before the
    /// reply — so the calls are gathered onto the reply they led to. Rendered message by message
    /// they appeared as separate boxes each announcing "1 tool call", which reads as the assistant
    /// having answered twice.
    /// </summary>
    public static List<TranscriptEntry> Build(IEnumerable<ChatMessage> history)
    {
        var entries = new List<TranscriptEntry>();
        var calledSoFar = new List<string>();

        foreach (var message in history)
        {
            if (message.Role == ChatRole.System || message.Role == ChatRole.Tool)
                continue;

            calledSoFar.AddRange(message.Contents
                .OfType<FunctionCallContent>()
                .Select(call => ToolLabel.Display(call.Name)));

            var text = message.Text ?? string.Empty;

            if (message.Role == ChatRole.User)
            {
                entries.Add(new TranscriptEntry(true, text, []));
            }
            else if (!string.IsNullOrWhiteSpace(text))
            {
                entries.Add(new TranscriptEntry(false, text, [.. calledSoFar]));
                calledSoFar.Clear();
            }
        }

        // Calls with no reply behind them yet: the turn is still running, or it failed after them.
        if (calledSoFar.Count > 0)
            entries.Add(new TranscriptEntry(false, string.Empty, [.. calledSoFar]));

        return entries;
    }
}
