using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AzureCliControlPanel.Core.Cli;

public static class AzCliPathLocator
{
    public static string? FindAzOnPath()
    {
        // On Windows, `where az` can return az.cmd, az.exe, or sometimes a shim.
        // We prefer az.cmd (official wrapper) then az.exe.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "az",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var p = Process.Start(psi);
            if (p is null) return null;

            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);

            var candidates = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (candidates.Count == 0) return null;

            // Prefer az.cmd, then az.exe, then first result.
            var cmd = candidates.FirstOrDefault(x => x.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase));
            if (cmd is not null) return cmd;

            var exe = candidates.FirstOrDefault(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (exe is not null) return exe;

            return candidates[0];
        }
        catch
        {
            return null;
        }
    }
}
