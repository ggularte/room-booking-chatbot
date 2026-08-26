namespace RoomBooking.Web;

/// <summary>
/// Cleans up a tool name for display.
///
/// Models sometimes emit their own control syntax inside the name they call: one produced
/// "list_my_bookings&lt;|channel|&gt;functions.list_my_bookings", which the transcript then showed
/// verbatim. The call itself failed and was retried, so nothing was lost but the appearance —
/// and control tokens on screen make the application look like it is coming apart.
/// </summary>
public static class ToolLabel
{
    private static readonly string[] Noise = ["<|", "|>", "\n", "\r"];

    public static string Display(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "tool";

        var cleaned = name.Trim();

        // Everything from the first control marker onwards is the model talking to itself. A name
        // that begins with one has nothing worth keeping, and reporting the token that followed it
        // as though it were a tool would be worse than admitting we do not know.
        var marker = cleaned.IndexOf("<|", StringComparison.Ordinal);
        if (marker >= 0)
            cleaned = cleaned[..marker];

        foreach (var noise in Noise)
            cleaned = cleaned.Replace(noise, string.Empty);

        // Some models qualify the name with the namespace they were given it under.
        const string qualifier = "functions.";
        if (cleaned.StartsWith(qualifier, StringComparison.Ordinal))
            cleaned = cleaned[qualifier.Length..];

        cleaned = cleaned.Trim();

        return cleaned.Length == 0 ? "tool" : cleaned;
    }
}
