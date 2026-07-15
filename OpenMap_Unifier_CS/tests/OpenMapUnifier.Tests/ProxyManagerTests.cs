using System.Net;
using OpenMapUnifier.Networking.Proxy;
using Xunit;

namespace OpenMapUnifier.Tests;

public class ProxyManagerTests
{
    [Theory]
    [InlineData("proxy.corp:8080", "http://proxy.corp:8080")]
    [InlineData("  http://proxy.corp:8080/  ", "http://proxy.corp:8080")]
    [InlineData("https://proxy.corp:3128", "https://proxy.corp:3128")]
    public void NormalizeProxyUrl_AddsSchemeAndTrims(string input, string expected)
    {
        Assert.Equal(expected, ProxyManager.NormalizeProxyUrl(input));
    }

    [Fact]
    public void SplitCredentials_ExtractsAndUnescapesUserInfo()
    {
        // Passwords with @ : / % — the special characters that used to break
        // the Python URL-embedding path and cause 407s.
        var (url, user, pass) = ProxyManager.SplitCredentials(
            "http://dom%5Cuser:p%40ss%3Aw0rd@proxy.corp:8080");
        Assert.Equal("http://proxy.corp:8080", url);
        Assert.Equal(@"dom\user", user);
        Assert.Equal("p@ss:w0rd", pass);
    }

    [Fact]
    public void SplitCredentials_NoCredentials_ReturnsUrlUnchanged()
    {
        var (url, user, pass) = ProxyManager.SplitCredentials("proxy.corp:8080");
        Assert.Equal("http://proxy.corp:8080", url);
        Assert.Null(user);
        Assert.Null(pass);
    }

    [Fact]
    public void CreateHandler_ManualProxy_SetsExplicitProxyWithCredentials()
    {
        var manager = new ProxyManager();
        manager.SetManualProxy("proxy.corp:8080", ProxyAuthType.Basic, "user", "p@ss!");
        manager.Config.NoProxy = "localhost,127.0.0.1,intranet.corp";

        using var handler = manager.CreateHandler();

        var proxy = Assert.IsType<WebProxy>(handler.Proxy);
        Assert.Equal(new Uri("http://proxy.corp:8080"), proxy.Address);
        var cred = proxy.Credentials?.GetCredential(proxy.Address!, "Basic");
        Assert.Equal("user", cred?.UserName);
        Assert.Equal("p@ss!", cred?.Password);
        Assert.True(proxy.IsBypassed(new Uri("http://intranet.corp/x")));
        Assert.False(proxy.IsBypassed(new Uri("https://download1.bayernwolke.de/a")));
    }

    [Fact]
    public void CreateHandler_NtlmDomain_SetsDomainCredential()
    {
        var manager = new ProxyManager();
        manager.SetManualProxy("proxy.corp:8080", ProxyAuthType.Ntlm, "user", "pw", "COMPANY");

        using var handler = manager.CreateHandler();

        var proxy = Assert.IsType<WebProxy>(handler.Proxy);
        var cred = proxy.Credentials?.GetCredential(proxy.Address!, "NTLM");
        Assert.Equal("COMPANY", cred?.Domain);
    }

    [Fact]
    public void CreateHandler_Disabled_LeavesEnvironmentFallback()
    {
        var manager = new ProxyManager();
        using var handler = manager.CreateHandler();
        // trust_env=True equivalent: no explicit proxy, default env behavior.
        Assert.Null(handler.Proxy);
        Assert.True(handler.UseProxy);
    }

    [Fact]
    public void Config_Json_NeverPersistsPassword()
    {
        var config = new ProxyConfig
        {
            Enabled = true,
            ProxyUrl = "http://proxy.corp:8080",
            AuthType = ProxyAuthType.Basic,
            Username = "user",
            Password = "SUPER-SECRET",
        };
        var json = config.ToJson();
        Assert.DoesNotContain("SUPER-SECRET", json);

        var restored = ProxyConfig.FromJson(json);
        Assert.Equal("user", restored.Username);
        Assert.Equal("", restored.Password);
        Assert.Equal(ProxyAuthType.Basic, restored.AuthType);
    }

    [Fact]
    public void ClassifyError_MapsProxyAuth407()
    {
        var e = new HttpRequestException("407", null, HttpStatusCode.ProxyAuthenticationRequired);
        Assert.Equal("PROXY_AUTH", ProxyManager.ClassifyError(e).Code);
    }

    [Fact]
    public void ClassifyError_MapsSslAndTimeoutAndDns()
    {
        var ssl = new HttpRequestException("handshake failed",
            new System.Security.Authentication.AuthenticationException("bad cert"));
        Assert.Equal("SSL", ProxyManager.ClassifyError(ssl).Code);

        Assert.Equal("TIMEOUT", ProxyManager.ClassifyError(new TaskCanceledException()).Code);

        var dns = new HttpRequestException("no such host",
            new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound));
        Assert.Equal("DNS", ProxyManager.ClassifyError(dns).Code);
    }

    [Fact]
    public void Diagnose_MasksCredentials()
    {
        var manager = new ProxyManager();
        manager.SetManualProxy("http://user:secret@proxy.corp:8080", ProxyAuthType.Basic, "user", "secret");
        var snapshot = manager.Diagnose();
        Assert.DoesNotContain("secret", snapshot);
        Assert.Contains("***:***@proxy.corp:8080", snapshot);
        Assert.Contains("password length : 6", snapshot);
    }

    [Fact]
    public void AutoDetect_KeepsManualConfigWhenNothingFound()
    {
        // Scoped env cleanup so the assertion can't be broken by ambient vars.
        var saved = new Dictionary<string, string?>();
        foreach (var name in new[] { "HTTPS_PROXY", "HTTP_PROXY", "https_proxy", "http_proxy" })
        {
            saved[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
        }
        try
        {
            var manager = new ProxyManager();
            manager.SetManualProxy("http://proxy.corp:8080");
            manager.AutoDetect();
            Assert.Equal("http://proxy.corp:8080", manager.Config.ProxyUrl);
            Assert.True(manager.Config.Enabled);
        }
        finally
        {
            foreach (var (name, value) in saved)
                Environment.SetEnvironmentVariable(name, value);
        }
    }
}
