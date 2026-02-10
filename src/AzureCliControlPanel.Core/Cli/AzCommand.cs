namespace AzureCliControlPanel.Core.Cli;

public sealed record AzCommand(string Verb, IReadOnlyList<string> Args, bool ExpectJson = true)
{
    public override string ToString()
        => "az " + Verb + (Args.Count == 0 ? string.Empty : " " + string.Join(" ", Args.Select(Escape)));

    private static string Escape(string s)
        => s.Contains(' ') ? $"\"{s.Replace("\"", "\\\"") }\"" : s;
}
