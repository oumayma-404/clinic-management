# ClinicManagement.DesktopShell

Thin **WPF + WebView2** client shell for the Local/offline-LAN deployment (Phase 5, FR-F). A staff PC runs this instead of a browser: it points a WebView2 control at the clinic server's HTTPS front door and renders the same Next.js web app. **Windows-only** (`net8.0-windows`), a **standalone solution** (`ClinicManagement.DesktopShell.sln`) — deliberately NOT part of `api/ClinicManagement.sln` (different SDK/target; keeps the backend build gate clean). Built/published only via `packaging/publish-server.ps1`.

## What it does
- Connects **only** to the Kestrel **HTTPS front door** (`https://<host>:<port>`, default port 5001) — never the internal Next web port. The server terminates TLS once and proxies pages itself, so the shell is a pure viewer.
- Stores the server address once per user and reuses it on every launch; changeable later without reinstalling.
- Shows friendly, recoverable French screens (configure / connecting / unreachable) instead of blank pages or raw browser errors.

## Files
| File | Role |
|------|------|
| `MainWindow.xaml` / `.cs` | The single window. Four mutually-exclusive view states — `WebView` / `Connecting` / `ServerConfig` / `Unreachable` — toggled by visibility. A top `Serveur` menu offers **Changer de serveur…** and **Recharger**. Initializes the WebView2 core against a **per-user** user-data folder (`%LocalAppData%\ClinicManagement\WebView2`) so a `%ProgramFiles%` install works without elevation. Uses `Navigate()` (not `Source=`) so Retry/Reload re-attempt. A failed `NavigationCompleted` → the unreachable screen. |
| `ServerConfig.cs` | `ServerConfig` (Host/Port → `BaseUrl`, HTTPS only) + `ServerConfigStore` persisting to `%AppData%\ClinicManagement\server.json` (corrupt/missing → first-run prompt). `ParseAddress` accepts a bare host, `host:port`, or a full URL. ⚠️ A **missing port is left unresolved**, not defaulted to 5001 — see `ServerProbe.cs`. |
| `ServerProbe.cs` | Settles an address that named no port, by trying `CandidatePorts` (**443 then 5001**) and taking the first that answers; the result is persisted as explicit so it costs one round trip per address. A TLS failure counts as « answered » — a self-signed LAN certificate is the expected case. **The rule is shared with both mobile shells** and is stated once in `mobile/CLAUDE.md` § « The port rule »; do not change it in one client alone. |
| `App.xaml` / `.cs` | Standard WPF entry point. |
| `ClinicManagement.DesktopShell.csproj` | `WinExe`, `net8.0-windows`, `UseWPF`; single `Microsoft.Web.WebView2` package reference. No RID pinned — `dotnet build` stays framework-dependent for review; the self-contained `win-x64` publish is driven by `packaging/publish-server.ps1`. |

## Gotchas
- Runtime requires the **Microsoft Edge WebView2 Runtime** on the target PC. The **client installer** (`packaging/client/clinic-client.iss`) bundles an offline standalone installer and runs it silently **only when the runtime is missing** (S7); a still-missing runtime surfaces as the unreachable screen, not a crash.
- Cannot be built/run in CI — needs Windows + the WebView2 runtime (operator-verified, R-1). It does `dotnet build` clean.
