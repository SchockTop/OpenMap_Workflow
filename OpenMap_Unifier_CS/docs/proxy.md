# Corporate proxy & TLS

`OpenMapUnifier.Core.Proxy.ProxyManager` is the C# port of the Python
Unifier's proxy layer (`backend/proxy_manager.py`), keeping the behaviors that
were hard-won there. Every HTTP client in the framework can be driven by it —
pass a `ProxyManager` via `DownloaderOptions.Proxy` or the provider factories,
and downloads, STAC/CKAN/API lookups and remote-zip extraction all share the
same proxy and TLS configuration.

## Quick start

```csharp
using OpenMapUnifier.Core.Proxy;

// Zero config: environment variables (HTTPS_PROXY/HTTP_PROXY) just work —
// pass no ProxyManager at all, or use one for diagnostics:
var proxy = new ProxyManager();
proxy.AutoDetect();

// Corporate proxy with Basic auth:
proxy.SetManualProxy("proxy.company.com:8080", ProxyAuthType.Basic, "user", "p@ss!word");

// NTLM (Windows domain) — native, no extra package:
proxy.SetManualProxy("proxy.company.com:8080", ProxyAuthType.Ntlm, "user", "pw", "COMPANY");

// TLS-inspecting proxy (corporate MITM): trust its root CA
proxy.SetSsl(sslVerify: true, caBundlePath: @"C:\corp\root-ca.pem");

using var downloader = new HttpTileDownloader(new DownloaderOptions { Proxy = proxy });
```

CLI: every command accepts `--proxy URL`, `--proxy-user U --proxy-pass P`,
`--proxy-domain DOMAIN` (switches to NTLM), `--no-proxy list`,
`--ca-bundle file.pem`, `--no-ssl-verify`.

## The design decisions (and why)

These are the lessons from debugging 407s in the Python Unifier, preserved:

1. **Credentials ride the CONNECT tunnel, never a header.** For HTTPS targets
   a session-wide `Proxy-Authorization` header is worse than useless: it sits
   *inside* the encrypted tunnel where the proxy can't read it, and leaks to
   the destination server (some CDNs treat it as an auth challenge). .NET's
   `WebProxy.Credentials` (a `NetworkCredential`) does the right thing.
2. **No URL-embedded credentials.** Passwords with `@ : / % !` broke the
   Python URL-encoding path repeatedly (the single most common 407 cause).
   Here credentials are structured data; a proxy URL that *contains*
   credentials is split apart and un-escaped (`SplitCredentials`), then
   re-attached properly.
3. **Explicit proxy ⇒ environment ignored** (`trust_env=False` semantics).
   Stale `HTTP_PROXY` values must not override configured credentials.
   Conversely, with NO explicit proxy, the environment stays active as the
   fallback so existing working setups keep working.
4. **Detection never destroys configuration.** `AutoDetect()` keeps a saved
   manual proxy when it finds nothing.
5. **CA bundle beats the verify toggle.** `CaBundlePath` re-validates the
   chain against your corporate root (`X509ChainTrustMode.CustomTrustStore`);
   `SslVerify=false` is the dev-only sledgehammer.
6. **Passwords are never persisted.** `ProxyConfig` serializes to
   `proxy_config.json` without the password (same rule as the Python config).

## Debugging a failing connection

```bash
openmap proxy-test --proxy proxy.corp:8080 --proxy-user u --proxy-pass p
```

prints (a) a credential-masked diagnostic snapshot — effective config plus
the proxy-relevant environment variables, so you can see *both* layers — and
(b) per-data-source connectivity results. Sources are tested separately
because corporate proxies often ACL per-URL: Bayern's WMS working proves
nothing about bayernwolke tile downloads.

Download failures are classified with the same codes as the Python Unifier
and surface in every error message:

| Code | Meaning | First thing to check |
|---|---|---|
| `PROXY_AUTH` | 407 from the proxy | username/password/auth type (Basic vs NTLM); run proxy-test |
| `SSL` | TLS handshake failed | TLS-inspecting proxy → set `--ca-bundle`; wrong bundle path |
| `PROXY` | can't reach the proxy | host/port typo, proxy down |
| `TIMEOUT` | no answer in time | proxy ACL silently dropping; huge file on slow link |
| `DNS` | name resolution failed | network/VPN down, wrong proxy for this network |
| `HTTP` | target server error | the data portal itself (404 = tile doesn't exist) |

Programmatic: `ProxyManager.ClassifyError(exception)` and
`ProxyManager.Diagnose()`.

## Persistence

```csharp
proxy.SaveTo(configDir);              // writes proxy_config.json (no password)
var restored = ProxyManager.LoadFrom(configDir);
restored.Config.Password = AskUser(); // re-supply at runtime
```
