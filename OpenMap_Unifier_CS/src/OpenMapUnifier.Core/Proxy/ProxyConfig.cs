using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenMapUnifier.Core.Proxy;

public enum ProxyAuthType
{
    None,
    Basic,
    /// <summary>Windows domain auth. Unlike the Python Unifier (which needed
    /// requests-ntlm and was unreliable over HTTPS proxies), .NET handles NTLM
    /// on the CONNECT tunnel natively via <see cref="System.Net.NetworkCredential"/>.</summary>
    Ntlm,
}

/// <summary>
/// Proxy configuration for corporate environments — a port of the Python
/// Unifier's ProxyConfig (backend/proxy_manager.py) with the same persistence
/// rules: the password is NEVER written to disk.
/// </summary>
public sealed class ProxyConfig
{
    public bool Enabled { get; set; }
    public bool AutoDetect { get; set; } = true;

    /// <summary>e.g. "http://proxy.company.com:8080" (scheme optional; added on normalize).</summary>
    public string ProxyUrl { get; set; } = "";

    public ProxyAuthType AuthType { get; set; } = ProxyAuthType.None;
    public string Username { get; set; } = "";

    [JsonIgnore] // never persisted — matches the Python Unifier's to_dict()
    public string Password { get; set; } = "";

    /// <summary>NTLM domain (e.g. "COMPANY").</summary>
    public string Domain { get; set; } = "";

    /// <summary>Comma-separated bypass list.</summary>
    public string NoProxy { get; set; } = "localhost,127.0.0.1";

    /// <summary>false = skip TLS verification (dev only — proxies that inspect HTTPS).</summary>
    public bool SslVerify { get; set; } = true;

    /// <summary>Absolute path to a PEM CA bundle; "" = system default. Takes
    /// precedence over <see cref="SslVerify"/>, same as the Python Unifier.</summary>
    public string CaBundlePath { get; set; } = "";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static ProxyConfig FromJson(string json) =>
        JsonSerializer.Deserialize<ProxyConfig>(json, JsonOptions) ?? new ProxyConfig();

    public void Save(string path) => File.WriteAllText(path, ToJson());

    public static ProxyConfig Load(string path) =>
        File.Exists(path) ? FromJson(File.ReadAllText(path)) : new ProxyConfig();
}
