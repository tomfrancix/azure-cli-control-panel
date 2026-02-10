using System.Text.Json;
using AzureCliControlPanel.Core.Cli;
using AzureCliControlPanel.Core.Models;
using AzureCliControlPanel.Core.Util;

namespace AzureCliControlPanel.Core.Services;

public sealed class AzureCliFacade
{
    private readonly IAzCliRunner _runner;
    private readonly AzureCliContext _context;
    private readonly SimpleMemoryCache _cache;
    private readonly IOutputSink _output;

    public AzureCliFacade(IAzCliRunner runner, AzureCliContext context, SimpleMemoryCache cache, IOutputSink output)
    {
        _runner = runner;
        _context = context;
        _cache = cache;
        _output = output;
    }

    public string? AzPath => _runner.AzPath;

    public async Task<(bool SignedIn, AzureAccount? Account, string? Error)> GetAccountAsync(CancellationToken ct)
    {
        var cmd = new AzCommand("account show", Array.Empty<string>());
        var res = await _runner.RunAsync(cmd, ct).ConfigureAwait(false);
        _output.Publish(res);

        if (!res.IsSuccess) return (false, null, BuildError(res));

        var doc = AzJson.TryParse(res.StdOut);
        if (doc is null) return (false, null, "Failed to parse az account show output.");

        var root = doc.RootElement;

        string? userName = null;
        string? userType = null;
        if (root.TryGetProperty("user", out var userEl))
        {
            if (userEl.TryGetProperty("name", out var n)) userName = n.GetString();
            if (userEl.TryGetProperty("type", out var t)) userType = t.GetString();
        }

        return (true, new AzureAccount(
            userName,
            userType,
            root.GetProperty("id").GetString() ?? string.Empty,
            root.GetProperty("name").GetString() ?? string.Empty,
            root.GetProperty("tenantId").GetString() ?? string.Empty,
            root.TryGetProperty("environmentName", out var env) ? env.GetString() : null
        ), null);
    }

    public async Task<IReadOnlyList<AzureSubscription>> ListSubscriptionsAsync(CancellationToken ct)
    {
        var cmd = new AzCommand("account list", Array.Empty<string>());
        var res = await RunWithRetryAsync(cmd, "account:list", allowCache: true, ct).ConfigureAwait(false);
        if (!res.IsSuccess) throw new InvalidOperationException(BuildError(res));

        var doc = AzJson.TryParse(res.StdOut) ?? throw new InvalidOperationException("Invalid JSON from az account list.");
        var list = new List<AzureSubscription>();

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            list.Add(new AzureSubscription(
                Id: el.GetProperty("id").GetString() ?? string.Empty,
                Name: el.GetProperty("name").GetString() ?? string.Empty,
                TenantId: el.GetProperty("tenantId").GetString() ?? string.Empty,
                State: el.TryGetProperty("state", out var st) ? st.GetString() ?? string.Empty : string.Empty,
                IsDefault: el.TryGetProperty("isDefault", out var d) && d.GetBoolean()
            ));
        }

        return list;
    }

    public async Task SwitchSubscriptionAsync(string subscriptionId, CancellationToken ct)
    {
        var cmd = new AzCommand("account set", new[] { "--subscription", subscriptionId }, ExpectJson: false);
        var res = await _runner.RunAsync(cmd, ct).ConfigureAwait(false);
        _output.Publish(res);
        if (!res.IsSuccess) throw new InvalidOperationException(BuildError(res));
        _cache.Clear();
    }

    public async Task LoginAsync(CancellationToken ct)
    {
        var cmd = new AzCommand("login", Array.Empty<string>(), ExpectJson: false);
        var res = await _runner.RunAsync(cmd, ct).ConfigureAwait(false);
        _output.Publish(res);
        if (!res.IsSuccess) throw new InvalidOperationException(BuildError(res));
        _cache.Clear();
    }

    public async Task LogoutAsync(CancellationToken ct)
    {
        var cmd = new AzCommand("logout", Array.Empty<string>(), ExpectJson: false);
        var res = await _runner.RunAsync(cmd, ct).ConfigureAwait(false);
        _output.Publish(res);
        if (!res.IsSuccess) throw new InvalidOperationException(BuildError(res));
        _cache.Clear();
    }

    public async Task<IReadOnlyList<AzureResourceGroup>> ListResourceGroupsAsync(CancellationToken ct)
    {
        var cmd = new AzCommand("group list", Array.Empty<string>());
        var res = await RunWithRetryAsync(cmd, "group:list", allowCache: true, ct).ConfigureAwait(false);
        if (!res.IsSuccess) throw new InvalidOperationException(BuildError(res));

        var doc = AzJson.TryParse(res.StdOut) ?? throw new InvalidOperationException("Invalid JSON from az group list.");
        var list = new List<AzureResourceGroup>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            list.Add(new AzureResourceGroup(
                Name: el.GetProperty("name").GetString() ?? string.Empty,
                Location: el.TryGetProperty("location", out var loc) ? loc.GetString() ?? string.Empty : string.Empty
            ));
        }
        return list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<AzureResource>> ListResourcesInGroupAsync(string rg, CancellationToken ct)
    {
        var cmd = new AzCommand("resource list", new[] { "-g", rg });
        var res = await RunWithRetryAsync(cmd, $"resource:list:{rg}", allowCache: true, ct).ConfigureAwait(false);
        if (!res.IsSuccess) throw new InvalidOperationException(BuildError(res));

        var doc = AzJson.TryParse(res.StdOut) ?? throw new InvalidOperationException("Invalid JSON from az resource list.");
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft.Web/sites",
            "Microsoft.Web/staticSites",
            "Microsoft.App/containerApps",
        };

        var list = new List<AzureResource>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var type = el.GetProperty("type").GetString() ?? string.Empty;
            if (!wanted.Contains(type)) continue;

            list.Add(new AzureResource(
                Id: el.GetProperty("id").GetString() ?? string.Empty,
                Name: el.GetProperty("name").GetString() ?? string.Empty,
                Type: type,
                Kind: el.TryGetProperty("kind", out var k) ? k.GetString() : null,
                ResourceGroup: rg,
                Location: el.TryGetProperty("location", out var loc) ? loc.GetString() ?? string.Empty : string.Empty
            ));
        }

        return list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<AppRuntimeInfo> GetWebAppRuntimeAsync(string rg, string name, CancellationToken ct)
    {
        var cmd = new AzCommand("webapp show", new[] { "-g", rg, "-n", name });
        var res = await RunWithRetryAsync(cmd, $"webapp:show:{rg}:{name}", allowCache: true, ct).ConfigureAwait(false);
        if (!res.IsSuccess) throw new InvalidOperationException(BuildError(res));

        var doc = AzJson.TryParse(res.StdOut) ?? throw new InvalidOperationException("Invalid JSON from az webapp show.");
        var root = doc.RootElement;

        var stateStr = root.TryGetProperty("state", out var st) ? st.GetString() : null;
        var state = stateStr?.Equals("Running", StringComparison.OrdinalIgnoreCase) == true ? AppRuntimeState.Running
                   : stateStr?.Equals("Stopped", StringComparison.OrdinalIgnoreCase) == true ? AppRuntimeState.Stopped
                   : AppRuntimeState.Unknown;

        var hosts = new List<string>();
        if (root.TryGetProperty("hostNames", out var h) && h.ValueKind == JsonValueKind.Array)
        {
            foreach (var he in h.EnumerateArray())
            {
                var s = he.GetString();
                if (!string.IsNullOrWhiteSpace(s)) hosts.Add(s);
            }
        }

        return new AppRuntimeInfo(state, hosts);
    }

    public async Task<AppIdentityInfo> GetWebAppIdentityAsync(string rg, string name, CancellationToken ct)
    {
        var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string? miPrincipalId = null;
        string? miTenantId = null;
        string? tenantFromAuth = null;
        string? clientFromAuth = null;

        // Managed identity
        {
            var cmd = new AzCommand("webapp show", new[] { "-g", rg, "-n", name });
            var res = await RunWithRetryAsync(cmd, $"webapp:show:{rg}:{name}", allowCache: true, ct).ConfigureAwait(false);
            if (res.IsSuccess)
            {
                var doc = AzJson.TryParse(res.StdOut);
                if (doc is not null && doc.RootElement.TryGetProperty("identity", out var idEl))
                {
                    if (idEl.TryGetProperty("principalId", out var p) && p.ValueKind == JsonValueKind.String)
                    {
                        miPrincipalId = p.GetString();
                        if (!string.IsNullOrWhiteSpace(miPrincipalId)) raw["managedIdentity.principalId"] = miPrincipalId!;
                    }
                    if (idEl.TryGetProperty("tenantId", out var t) && t.ValueKind == JsonValueKind.String)
                    {
                        miTenantId = t.GetString();
                        if (!string.IsNullOrWhiteSpace(miTenantId)) raw["managedIdentity.tenantId"] = miTenantId!;
                    }
                }
            }
        }

        // EasyAuth / Entra
        {
            var cmd = new AzCommand("webapp auth show", new[] { "-g", rg, "-n", name });
            var res = await RunWithRetryAsync(cmd, $"webapp:auth:{rg}:{name}", allowCache: true, ct).ConfigureAwait(false);
            if (res.IsSuccess)
            {
                var doc = AzJson.TryParse(res.StdOut);
                if (doc is not null)
                {
                    var root = doc.RootElement;

                    if (root.TryGetProperty("clientId", out var c) && c.ValueKind == JsonValueKind.String)
                        clientFromAuth = c.GetString();

                    if (root.TryGetProperty("issuer", out var iss) && iss.ValueKind == JsonValueKind.String)
                    {
                        var issuer = iss.GetString();
                        if (!string.IsNullOrWhiteSpace(issuer))
                        {
                            raw["auth.issuer"] = issuer!;
                            tenantFromAuth = TryExtractTenantIdFromIssuer(issuer!);
                        }
                    }

                    if (root.TryGetProperty("identityProviders", out var ip) && ip.ValueKind == JsonValueKind.Object &&
                        ip.TryGetProperty("azureActiveDirectory", out var aad) && aad.ValueKind == JsonValueKind.Object &&
                        aad.TryGetProperty("registration", out var reg) && reg.ValueKind == JsonValueKind.Object)
                    {
                        if (reg.TryGetProperty("clientId", out var cc) && cc.ValueKind == JsonValueKind.String)
                            clientFromAuth ??= cc.GetString();

                        if (reg.TryGetProperty("openIdIssuer", out var oi) && oi.ValueKind == JsonValueKind.String)
                        {
                            var issuer = oi.GetString();
                            if (!string.IsNullOrWhiteSpace(issuer))
                            {
                                raw["auth.openIdIssuer"] = issuer!;
                                tenantFromAuth ??= TryExtractTenantIdFromIssuer(issuer!);
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(clientFromAuth)) raw["auth.clientId"] = clientFromAuth!;
                    if (!string.IsNullOrWhiteSpace(tenantFromAuth)) raw["auth.tenantId"] = tenantFromAuth!;
                }
            }
        }

        var acct = await GetAccountAsync(ct).ConfigureAwait(false);
        var tenantId = tenantFromAuth ?? miTenantId ?? acct.Account?.TenantId ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(clientFromAuth))
        {
            return new AppIdentityInfo(tenantId, clientFromAuth, miPrincipalId, "EasyAuth/Entra ID", raw);
        }

        if (!string.IsNullOrWhiteSpace(miPrincipalId))
        {
            return new AppIdentityInfo(tenantId, null, miPrincipalId, "Managed Identity (System-assigned)", raw);
        }

        return new AppIdentityInfo(tenantId, null, null, "N/A (no AAD app configured)", raw);
    }

    public async Task WebAppStartAsync(string rg, string name, CancellationToken ct)
        => await RunNonQueryAsync(new AzCommand("webapp start", new[] { "-g", rg, "-n", name }, ExpectJson: false), ct).ConfigureAwait(false);

    public async Task WebAppStopAsync(string rg, string name, CancellationToken ct)
        => await RunNonQueryAsync(new AzCommand("webapp stop", new[] { "-g", rg, "-n", name }, ExpectJson: false), ct).ConfigureAwait(false);

    public async Task WebAppRestartAsync(string rg, string name, CancellationToken ct)
        => await RunNonQueryAsync(new AzCommand("webapp restart", new[] { "-g", rg, "-n", name }, ExpectJson: false), ct).ConfigureAwait(false);

    public Task<IAsyncEnumerable<string>> WebAppLogTailAsync(string rg, string name, CancellationToken ct)
        => _runner.RunStreamingAsync(new AzCommand("webapp log tail", new[] { "-g", rg, "-n", name }, ExpectJson: false), ct);

    public async Task<string> WebAppLogConfigAsync(string rg, string name, string level, CancellationToken ct)
    {
        var cmd = new AzCommand("webapp log config", new[]
        {
            "-g", rg, "-n", name,
            "--application-logging", "filesystem",
            "--level", level,
            "--web-server-logging", "filesystem",
            "--detailed-error-messages", "true",
            "--failed-request-tracing", "true"
        });

        var res = await _runner.RunAsync(cmd, ct).ConfigureAwait(false);
        _output.Publish(res);
        if (!res.IsSuccess) throw new InvalidOperationException(BuildError(res));

        _cache.InvalidateByPrefix("webapp:");
        return res.StdOut;
    }

    public async Task<AppRuntimeInfo> GetContainerAppRuntimeAsync(string rg, string name, CancellationToken ct)
    {
        var cmd = new AzCommand("containerapp show", new[] { "-g", rg, "-n", name });
        var res = await RunWithRetryAsync(cmd, $"containerapp:show:{rg}:{name}", allowCache: true, ct).ConfigureAwait(false);
        if (!res.IsSuccess) throw new InvalidOperationException(BuildError(res));

        var doc = AzJson.TryParse(res.StdOut) ?? throw new InvalidOperationException("Invalid JSON from az containerapp show.");
        var root = doc.RootElement;

        var hasRev = root.TryGetProperty("properties", out var props)
                     && props.TryGetProperty("latestRevisionName", out var lrn)
                     && lrn.ValueKind == JsonValueKind.String
                     && !string.IsNullOrWhiteSpace(lrn.GetString());

        return new AppRuntimeInfo(hasRev ? AppRuntimeState.Running : AppRuntimeState.Unknown, Array.Empty<string>());
    }

    public Task<IAsyncEnumerable<string>> ContainerAppLogsAsync(string rg, string name, CancellationToken ct)
        => _runner.RunStreamingAsync(new AzCommand("containerapp logs show", new[] { "-g", rg, "-n", name, "--follow" }, ExpectJson: false), ct);

    public async Task ContainerAppRestartAsync(string rg, string name, CancellationToken ct)
    {
        var listCmd = new AzCommand("containerapp revision list", new[] { "-g", rg, "-n", name });
        var listRes = await _runner.RunAsync(listCmd, ct).ConfigureAwait(false);
        _output.Publish(listRes);
        if (!listRes.IsSuccess) throw new InvalidOperationException(BuildError(listRes));

        var doc = AzJson.TryParse(listRes.StdOut) ?? throw new InvalidOperationException("Invalid JSON from az containerapp revision list.");
        var latest = doc.RootElement.EnumerateArray()
            .Select(x => new
            {
                Name = x.TryGetProperty("name", out var n) ? n.GetString() : null,
                Created = x.TryGetProperty("properties", out var p) && p.TryGetProperty("createdTime", out var c) ? c.GetDateTimeOffset() : (DateTimeOffset?)null
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .OrderByDescending(x => x.Created ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        if (latest is null) throw new InvalidOperationException("No container app revisions found.");

        var restartCmd = new AzCommand("containerapp revision restart", new[] { "-g", rg, "--name", name, "--revision", latest.Name! }, ExpectJson: false);
        var res = await _runner.RunAsync(restartCmd, ct).ConfigureAwait(false);
        _output.Publish(res);
        if (!res.IsSuccess) throw new InvalidOperationException(BuildError(res));

        _cache.InvalidateByPrefix("containerapp:");
    }

    public async Task<string> BuildPortalLinkAsync(string resourceId, CancellationToken ct)
    {
        var acct = await GetAccountAsync(ct).ConfigureAwait(false);
        var tenant = acct.Account?.TenantId;
        if (string.IsNullOrWhiteSpace(tenant))
            return $"https://portal.azure.com/#resource{resourceId}";
        return $"https://portal.azure.com/#@{tenant}/resource{resourceId}";
    }

    private async Task RunNonQueryAsync(AzCommand cmd, CancellationToken ct)
    {
        var res = await _runner.RunAsync(cmd, ct).ConfigureAwait(false);
        _output.Publish(res);
        if (!res.IsSuccess) throw new InvalidOperationException(BuildError(res));
        _cache.InvalidateByPrefix("webapp:");
    }

    private async Task<AzResult> RunWithRetryAsync(AzCommand cmd, string cacheKey, bool allowCache, CancellationToken ct)
    {
        if (allowCache && _cache.TryGet<AzResult>(cacheKey, out var cached) && cached is not null)
        {
            _output.Publish(cached);
            return cached;
        }

        var res = await Retry.WithBackoffAsync(
            async token =>
            {
                var r = await _runner.RunAsync(cmd, token).ConfigureAwait(false);
                _output.Publish(r);
                return r;
            },
            maxAttempts: 3,
            isSuccess: r => r.IsSuccess,
            initialDelay: TimeSpan.FromMilliseconds(250),
            cancellationToken: ct
        ).ConfigureAwait(false);

        if (allowCache && res.IsSuccess)
            _cache.Set(cacheKey, res, _context.CacheTtl);

        return res;
    }

    private static string BuildError(AzResult res)
    {
        var err = Redaction.Redact((res.StdErr ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(err))
            err = Redaction.Redact((res.StdOut ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(err))
            err = "Azure CLI returned a non-zero exit code with no output.";
        return $"{res.Command}\nExitCode={res.ExitCode}\n{err}";
    }

    private static string? TryExtractTenantIdFromIssuer(string issuer)
    {
        try
        {
            var uri = new Uri(issuer);
            var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (uri.Host.Equals("sts.windows.net", StringComparison.OrdinalIgnoreCase))
                return parts.FirstOrDefault();
            if (uri.Host.Contains("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase))
                return parts.FirstOrDefault();
        }
        catch { }
        return null;
    }
}
