using System.Text.RegularExpressions;

namespace AzureCliControlPanel.Core.Cli;

public static partial class Redaction
{
    //[GeneratedRegex("(\"access_token\"\\s*:\\s*\")([^\"]+)(\")", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    //private static partial Regex AccessTokenRegex();

    [GeneratedRegex("(\"refresh_token\"\\s*:\\s*\")([^\"]+)(\")", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex RefreshTokenRegex();

    [GeneratedRegex("(\"id_token\"\\s*:\\s*\")([^\"]+)(\")", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex IdTokenRegex();

    [GeneratedRegex("(Bearer\\s+)([A-Za-z0-9\\-\\._~\\+\\/]+=*)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex BearerRegex();

    [GeneratedRegex("(AADSTS\\d+:[^\\n]*?)([A-Za-z0-9\\-\\._~\\+\\/]+=*)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex AadstsRegex();

    private static readonly Regex[] Patterns =
    [
       // AccessTokenRegex(),
        RefreshTokenRegex(),
        IdTokenRegex(),
        BearerRegex(),
        AadstsRegex(),
    ];

    public static string Redact(string input)
    {
        var s = input ?? string.Empty;
        foreach (var p in Patterns)
        {
            s = p.Replace(s, m =>
            {
                if (m.Groups.Count >= 4)
                {
                    return m.Groups[1].Value + "***REDACTED***" + m.Groups[^1].Value;
                }
                if (m.Groups.Count >= 3)
                {
                    return m.Groups[1].Value + "***REDACTED***";
                }
                return "***REDACTED***";
            });
        }
        return s;
    }
}