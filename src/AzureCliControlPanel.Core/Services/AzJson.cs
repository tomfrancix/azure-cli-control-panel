using System.Text.Json;

namespace AzureCliControlPanel.Core.Services;

public static class AzJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static JsonDocument? TryParse(string json)
    {
        try { return JsonDocument.Parse(json); }
        catch { return null; }
    }
}
