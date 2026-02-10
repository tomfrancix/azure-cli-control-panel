using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AzureCliControlPanel.Core.Cli;

public sealed class AzCliRunner : IAzCliRunner
{
    private readonly string? _azPath;
    public string? AzPath => _azPath;

    public AzCliRunner(string? azPathOverride = null)
    {
        _azPath = string.IsNullOrWhiteSpace(azPathOverride)
            ? AzCliPathLocator.FindAzOnPath()
            : azPathOverride;
    }

    public async Task<AzResult> RunAsync(AzCommand command, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var (fileName, arguments) = BuildStartInfo(command);

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            var stdoutTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stderrTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) stdoutTcs.TrySetResult(true);
                else stdout.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) stderrTcs.TrySetResult(true);
                else stderr.AppendLine(e.Data);
            };

            if (!process.Start())
                return new AzResult(command, 1, string.Empty, "Failed to start Azure CLI process.", sw.Elapsed);

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(stdoutTcs.Task, stderrTcs.Task).ConfigureAwait(false);

            sw.Stop();
            return new AzResult(command, process.ExitCode, stdout.ToString(), stderr.ToString(), sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new AzResult(command, -1, string.Empty, "Cancelled.", sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new AzResult(command, 1, string.Empty, $"Exception running Azure CLI: {ex}", sw.Elapsed);
        }
    }

    public Task<IAsyncEnumerable<string>> RunStreamingAsync(AzCommand command, CancellationToken cancellationToken)
    {
        var ch = System.Threading.Channels.Channel.CreateUnbounded<string>();

        var (fileName, arguments) = BuildStartInfo(command);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        _ = Task.Run(async () =>
        {
            try
            {
                if (!process.Start())
                {
                    await ch.Writer.WriteAsync("Failed to start Azure CLI process.", CancellationToken.None);
                    ch.Writer.TryComplete();
                    return;
                }

                async Task PumpAsync(StreamReader reader, string prefix)
                {
                    while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line is null) break;
                        await ch.Writer.WriteAsync(prefix + line, cancellationToken).ConfigureAwait(false);
                    }
                }

                await Task.WhenAll(
                    PumpAsync(process.StandardOutput, ""),
                    PumpAsync(process.StandardError, "[stderr] ")
                ).ConfigureAwait(false);

                try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); } catch { }
                ch.Writer.TryComplete();
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                ch.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                try { await ch.Writer.WriteAsync("[exception] " + ex, CancellationToken.None).ConfigureAwait(false); } catch { }
                ch.Writer.TryComplete(ex);
            }
            finally
            {
                process.Dispose();
            }
        }, CancellationToken.None);

        async IAsyncEnumerable<string> Enumerate()
        {
            await foreach (var item in ch.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }

        return Task.FromResult((IAsyncEnumerable<string>)Enumerate());
    }

    private (string FileName, string Arguments) BuildStartInfo(AzCommand command)
    {
        var azExe = ResolveAzExecutablePath(_azPath);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && IsCmdScript(azExe))
        {
            // Build args for cmd.exe (NOT CreateProcess quoting).
            var cmdArgs = BuildAzArgumentsForCmd(command);

            // EXACT pattern:
            // cmd.exe /d /s /c ""C:\Path With Spaces\az.cmd" <args...>"
            //
            // IMPORTANT: we must close the quote after az.cmd BEFORE args start.
            // That is: ""<path>\az.cmd" <args...>"
            var arguments = $"/d /s /c \"\"{azExe}\" {cmdArgs}\"";

            return ("cmd.exe", arguments);
        }

        // Direct execution (az.exe, or non-Windows)
        var directArgs = BuildAzArgumentsForDirectExec(command);
        return (azExe, directArgs);
    }

    private static string BuildAzArgumentsForDirectExec(AzCommand command)
    {
        var args = new List<string>();

        // FIX: split verb into tokens (e.g., "account show" -> ["account","show"])
        args.AddRange(SplitCliTokens(command.Verb));

        // Existing args
        args.AddRange(command.Args);

        if (command.ExpectJson && !args.Contains("-o") && !args.Contains("--output"))
        {
            args.Add("-o");
            args.Add("json");
        }

        // CreateProcess quoting
        return string.Join(" ", args.Select(QuoteIfNeededForDirectExec));
    }

    private static string BuildAzArgumentsForCmd(AzCommand command)
    {
        var args = new List<string>();

        // FIX: split verb into tokens (e.g., "webapp log tail" -> ["webapp","log","tail"])
        args.AddRange(SplitCliTokens(command.Verb));

        // Existing args
        args.AddRange(command.Args);

        if (command.ExpectJson && !args.Contains("-o") && !args.Contains("--output"))
        {
            args.Add("-o");
            args.Add("json");
        }

        // cmd.exe token quoting
        return string.Join(" ", args.Select(QuoteIfNeededForCmdToken));
    }


    private static string ResolveAzExecutablePath(string? azPath)
    {
        if (string.IsNullOrWhiteSpace(azPath))
            return "az";

        if (File.Exists(azPath))
        {
            var ext = Path.GetExtension(azPath);
            if (string.IsNullOrWhiteSpace(ext))
            {
                var cmd = azPath + ".cmd";
                if (File.Exists(cmd)) return cmd;

                var exe = azPath + ".exe";
                if (File.Exists(exe)) return exe;
            }

            if (Path.GetFileName(azPath).Equals("az", StringComparison.OrdinalIgnoreCase))
            {
                var siblingCmd = Path.Combine(Path.GetDirectoryName(azPath)!, "az.cmd");
                if (File.Exists(siblingCmd)) return siblingCmd;

                var siblingExe = Path.Combine(Path.GetDirectoryName(azPath)!, "az.exe");
                if (File.Exists(siblingExe)) return siblingExe;
            }

            return azPath;
        }

        return "az";
    }

    private static bool IsCmdScript(string path)
        => path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

    private static string QuoteIfNeededForDirectExec(string s)
        => s.Contains(' ') || s.Contains('"') ? QuoteForDirectExec(s) : s;

    private static string QuoteForDirectExec(string s)
        => "\"" + s.Replace("\"", "\\\"") + "\"";

    private static string QuoteIfNeededForCmdToken(string s)
    {
        // cmd.exe: if it needs quoting, wrap in quotes and double embedded quotes.
        if (s.IndexOfAny(new[] { ' ', '\t' }) >= 0 || s.Contains('"'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";

        return s;
    }
    private static IReadOnlyList<string> SplitCliTokens(string verb)
    {
        if (string.IsNullOrWhiteSpace(verb))
            return Array.Empty<string>();

        // For our usage, verbs are CLI segments separated by spaces.
        // e.g. "account show" / "webapp log tail" / "containerapp logs show"
        return verb
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

}
