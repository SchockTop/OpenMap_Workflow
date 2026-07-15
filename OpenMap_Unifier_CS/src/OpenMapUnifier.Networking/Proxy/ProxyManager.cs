using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace OpenMapUnifier.Networking.Proxy;

/// <summary>
/// Proxy detection and HTTP handler configuration for corporate environments —
/// the C# counterpart of the Python Unifier's ProxyManager, with the same
/// hard-won specifics:
/// <list type="bullet">
/// <item>Credentials are attached to the proxy itself (sent on the CONNECT
/// tunnel), never as a session-wide Proxy-Authorization header — for HTTPS
/// that header sits inside the encrypted tunnel where the proxy can't see it,
/// and worse, leaks to the destination server.</item>
/// <item>With an explicit manual proxy, environment variables are ignored
/// (Python's trust_env=False) so stale HTTP_PROXY values can't override the
/// configured credentials and cause 407s. Without one, the environment is the
/// fallback so pre-existing working setups keep working.</item>
/// <item>SSL: an explicit CA bundle path beats the verify toggle; disabling
/// verification is supported for TLS-inspecting proxies (dev only).</item>
/// </list>
/// </summary>
public sealed class ProxyManager
{
    public const string DefaultConfigFileName = "proxy_config.json";

    public ProxyConfig Config { get; private set; }
    public string LastDetectMessage { get; private set; } = "";

    /// <summary>Targets for connection tests — one per data source, because
    /// corporate proxies can have per-URL ACLs that pass one and 407 the other.</summary>
    public static readonly IReadOnlyDictionary<string, string> TestTargets = new Dictionary<string, string>
    {
        ["Bayern (geoservices.bayern.de)"] = "https://geoservices.bayern.de",
        ["Bayern tiles (download1.bayernwolke.de)"] = "https://download1.bayernwolke.de",
        ["Niedersachsen STAC (dgm.stac.lgln.niedersachsen.de)"] = "https://dgm.stac.lgln.niedersachsen.de/collections",
    };

    public ProxyManager(ProxyConfig? config = null)
    {
        Config = config ?? new ProxyConfig();
    }

    public static ProxyManager LoadFrom(string configDir)
    {
        var manager = new ProxyManager(ProxyConfig.Load(Path.Combine(configDir, DefaultConfigFileName)));
        return manager;
    }

    public void SaveTo(string configDir) =>
        Config.Save(Path.Combine(configDir, DefaultConfigFileName));

    // ---- detection -----------------------------------------------------------

    /// <summary>HTTPS_PROXY / HTTP_PROXY (upper- then lowercase), like the Python Unifier.</summary>
    public static string? DetectFromEnvironment()
    {
        foreach (var name in new[] { "HTTPS_PROXY", "HTTP_PROXY", "https_proxy", "http_proxy" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }

    /// <summary>
    /// Auto-detect from the environment. Returns true when a proxy was found.
    /// A previously configured manual proxy is kept intact when nothing is
    /// detected — detection never wipes user settings.
    /// </summary>
    public bool AutoDetect()
    {
        var detected = DetectFromEnvironment();
        if (detected is not null)
        {
            Config.ProxyUrl = detected;
            Config.AutoDetect = true;
            Config.Enabled = true;
            LastDetectMessage = $"Detected proxy: {detected}";
            return true;
        }

        if (Config is { Enabled: true, AutoDetect: false } && Config.ProxyUrl.Length > 0)
        {
            LastDetectMessage = "Auto-detect found no proxy. Keeping your saved manual configuration.";
            return false;
        }

        LastDetectMessage = "No proxy detected. Using direct connection (environment fallback stays active).";
        Config.Enabled = false;
        return false;
    }

    public void SetManualProxy(string proxyUrl, ProxyAuthType authType = ProxyAuthType.None,
        string username = "", string password = "", string domain = "")
    {
        Config.Enabled = proxyUrl.Length > 0;
        Config.AutoDetect = false;
        Config.ProxyUrl = proxyUrl;
        Config.AuthType = authType;
        Config.Username = username;
        Config.Password = password;
        Config.Domain = domain;
    }

    public void DisableProxy() => Config.Enabled = false;

    public void SetSsl(bool sslVerify, string caBundlePath = "")
    {
        Config.SslVerify = sslVerify;
        Config.CaBundlePath = caBundlePath;
    }

    // ---- URL plumbing --------------------------------------------------------

    /// <summary>Add a scheme if missing, strip whitespace and trailing slashes.</summary>
    public static string NormalizeProxyUrl(string proxyUrl)
    {
        proxyUrl = proxyUrl.Trim().TrimEnd('/');
        if (proxyUrl.Length > 0 && !proxyUrl.Contains("://"))
            proxyUrl = "http://" + proxyUrl;
        return proxyUrl;
    }

    /// <summary>
    /// Strip any credentials pasted into the proxy URL itself. In .NET the
    /// credentials go on <see cref="WebProxy.Credentials"/> as a
    /// NetworkCredential — never percent-encoded into the URL — which sidesteps
    /// the special-character encoding bugs that caused most 407s in Python.
    /// </summary>
    public static (string Url, string? UserFromUrl, string? PasswordFromUrl) SplitCredentials(string proxyUrl)
    {
        proxyUrl = NormalizeProxyUrl(proxyUrl);
        var schemeEnd = proxyUrl.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) return (proxyUrl, null, null);
        var rest = proxyUrl[(schemeEnd + 3)..];
        var at = rest.LastIndexOf('@');
        if (at < 0) return (proxyUrl, null, null);

        var creds = rest[..at];
        var host = rest[(at + 1)..];
        var colon = creds.IndexOf(':');
        var user = Uri.UnescapeDataString(colon < 0 ? creds : creds[..colon]);
        var pass = colon < 0 ? null : Uri.UnescapeDataString(creds[(colon + 1)..]);
        return (proxyUrl[..(schemeEnd + 3)] + host, user, pass);
    }

    // ---- handler construction --------------------------------------------------

    /// <summary>
    /// Build a fully configured <see cref="SocketsHttpHandler"/>. Use this for
    /// every HTTP client in the pipeline so proxy + TLS behavior stays uniform
    /// (the Python Unifier routed everything through one requests.Session for
    /// the same reason).
    /// </summary>
    public SocketsHttpHandler CreateHandler()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        if (Config.Enabled && Config.ProxyUrl.Length > 0)
        {
            var (url, userFromUrl, passFromUrl) = SplitCredentials(Config.ProxyUrl);
            var bypass = Config.NoProxy
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(host => host.Replace(".", "\\."))
                .ToArray();
            var proxy = new WebProxy(url, BypassOnLocal: true, bypass);

            var username = Config.Username.Length > 0 ? Config.Username : userFromUrl;
            var password = Config.Password.Length > 0 ? Config.Password : passFromUrl;
            if (Config.AuthType != ProxyAuthType.None && !string.IsNullOrEmpty(username))
            {
                proxy.Credentials = Config.AuthType == ProxyAuthType.Ntlm && Config.Domain.Length > 0
                    ? new NetworkCredential(username, password, Config.Domain)
                    : new NetworkCredential(username, password);
            }

            // Explicit proxy — env vars must not override it (trust_env=False).
            handler.Proxy = proxy;
            handler.UseProxy = true;
        }
        // else: leave handler defaults — HttpClient.DefaultProxy honours
        // HTTPS_PROXY/HTTP_PROXY, the trust_env=True fallback path.

        ConfigureTls(handler);
        return handler;
    }

    private void ConfigureTls(SocketsHttpHandler handler)
    {
        if (Config.CaBundlePath.Length > 0 && File.Exists(Config.CaBundlePath))
        {
            var trustStore = new X509Certificate2Collection();
            trustStore.ImportFromPemFile(Config.CaBundlePath);
            handler.SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, cert, _, errors) =>
                {
                    if (errors == SslPolicyErrors.None) return true;
                    if (cert is null) return false;
                    // Re-validate against the custom bundle (corporate MITM root).
                    using var chain = new X509Chain();
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.AddRange(trustStore);
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    return chain.Build(new X509Certificate2(cert));
                },
            };
        }
        else if (!Config.SslVerify)
        {
            handler.SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = static (_, _, _, _) => true,
            };
        }
    }

    public HttpClient CreateClient(TimeSpan? timeout = null)
    {
        var client = new HttpClient(CreateHandler());
        if (timeout is { } t) client.Timeout = t;
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) OpenMapUnifier/1.0");
        return client;
    }

    // ---- error classification ---------------------------------------------------

    /// <summary>
    /// Map a network exception to (code, user message) — same codes as the
    /// Python Unifier: PROXY_AUTH | SSL | PROXY | TIMEOUT | DNS | HTTP | OTHER.
    /// </summary>
    public static (string Code, string Message) ClassifyError(Exception exception)
    {
        var text = exception.ToString();

        if (exception is HttpRequestException { StatusCode: HttpStatusCode.ProxyAuthenticationRequired }
            || text.Contains("407"))
            return ("PROXY_AUTH",
                "Proxy rejected credentials (407). Check username/password/auth type (Basic vs NTLM).");

        if (FindInner<AuthenticationException>(exception) is not null)
            return ("SSL",
                "SSL error — set a CA bundle (.pem), or disable SSL verify if your proxy inspects HTTPS.");

        if (exception is TaskCanceledException or TimeoutException)
            return ("TIMEOUT",
                "Timed out — the proxy or target may be slow, blocking, or requires authentication.");

        if (FindInner<SocketException>(exception) is { } socket)
        {
            return socket.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData
                ? ("DNS", "Cannot resolve host — check network, DNS, and proxy settings.")
                : ("PROXY", "Connection failed — check the proxy URL is reachable and host/port are correct.");
        }

        if (exception is HttpRequestException { StatusCode: { } status })
            return ("HTTP", $"HTTP {(int)status}: {exception.Message}");

        return ("OTHER", exception.Message);
    }

    private static T? FindInner<T>(Exception? e) where T : Exception
    {
        while (e is not null)
        {
            if (e is T match) return match;
            e = e.InnerException;
        }
        return null;
    }

    // ---- testing & diagnostics ----------------------------------------------------

    public async Task<IReadOnlyDictionary<string, (bool Ok, string Message)>> TestConnectionsAsync(
        CancellationToken ct = default)
    {
        using var client = CreateClient(TimeSpan.FromSeconds(15));
        var results = new Dictionary<string, (bool, string)>();
        foreach (var (label, url) in TestTargets)
        {
            try
            {
                using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
                results[label] = (int)response.StatusCode < 400
                    ? (true, $"OK (HTTP {(int)response.StatusCode})")
                    : (false, $"HTTP {(int)response.StatusCode}");
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                var (code, message) = ClassifyError(e);
                results[label] = (false, $"[{code}] {message}");
            }
        }
        return results;
    }

    /// <summary>
    /// Non-secret snapshot of the effective proxy state for debugging 407s —
    /// passwords are masked, only their length is reported.
    /// </summary>
    public string Diagnose()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[PROXY] DIAGNOSTIC SNAPSHOT");
        sb.AppendLine($"  enabled         : {Config.Enabled}");
        sb.AppendLine($"  auto_detect     : {Config.AutoDetect}");
        sb.AppendLine($"  proxy_url       : {MaskCredentials(Config.ProxyUrl)}");
        sb.AppendLine($"  auth_type       : {Config.AuthType}");
        sb.AppendLine($"  username        : {Config.Username}");
        sb.AppendLine($"  password length : {Config.Password.Length}");
        sb.AppendLine($"  domain          : {Config.Domain}");
        sb.AppendLine($"  no_proxy        : {Config.NoProxy}");
        sb.AppendLine($"  ssl_verify      : {Config.SslVerify}");
        sb.AppendLine($"  ca_bundle_path  : {(Config.CaBundlePath.Length > 0 ? Config.CaBundlePath : "(system CAs)")}");
        var envVars = new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy", "NO_PROXY", "no_proxy" }
            .Where(name => Environment.GetEnvironmentVariable(name) is not null)
            .Select(name => $"{name}={MaskCredentials(Environment.GetEnvironmentVariable(name)!)}");
        sb.AppendLine($"  env proxy vars  : {string.Join(", ", envVars.DefaultIfEmpty("(none set)"))}");
        return sb.ToString();
    }

    public static string MaskCredentials(string url)
    {
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) return url;
        var rest = url[(schemeEnd + 3)..];
        var at = rest.LastIndexOf('@');
        return at < 0 ? url : $"{url[..(schemeEnd + 3)]}***:***@{rest[(at + 1)..]}";
    }
}
