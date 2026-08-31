using System;
using System.Collections.Generic;
using Microsoft.Web.WebView2.Core;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// The shell's bridge to the page — <b>its first</b>. Until the coffre this shell was indistinguishable from a
/// plain browser: it exposed no <c>window.__clinicShell</c> at all, which is why its version floor had to be read
/// over native HTTP before navigation instead of from the object every other client carries.
///
/// <para>It exposes exactly two facts and one seam: <c>version</c>, <c>platform: "windows"</c>, and the coffre
/// folder. None of the mobile members are here — this shell has no <c>saveFile</c> (a WebView2 download works),
/// no <c>print</c> (the page's own works) and no biometric prompt.</para>
///
/// <para>⚠️ <b>The handle arrives with its permission already granted.</b>
/// <c>CreateWebFileSystemDirectoryHandle</c> hands web content a real <c>FileSystemDirectoryHandle</c> whose
/// <c>PermissionStatus</c> is <c>granted</c>, so the page never calls <c>requestPermission</c>, no picker opens and
/// no prompt appears. That is the whole reason a doctor's 400 Mo study can be filed in two clicks rather than
/// after a folder-picking ceremony on every machine.</para>
///
/// <para>⚠️ <b>The origin is checked before the handle is posted.</b> This object gives the page unusual reach into
/// the file system, and Microsoft's own guidance is explicit: post it only to content you intended. A page
/// navigated somewhere else must never receive the cabinet's coffre.</para>
/// </summary>
public static class VaultBridge
{
    /// <summary>
    /// The bridge itself, injected before any page script runs.
    ///
    /// <para>⚠️ <b><c>__clinicShellPendingVault</c> exists because the two sides race.</b> The shell posts the
    /// handle when navigation completes; the page's own listener is installed whenever its bundle happens to
    /// evaluate. Whichever arrives first must not lose the handle, so the script hands it over if the page is
    /// ready and parks it if not — and the page checks the parking slot on start-up.</para>
    ///
    /// <para>⚠️ Top frame only, and everything is wrapped: a page that somehow breaks this must lose the coffre,
    /// never its own scripts.</para>
    /// </summary>
    public static string Script(string version) => $$"""
        (function () {
          try {
            if (window.top !== window) { return; }

            Object.defineProperty(window, '__clinicShell', {
              value: Object.freeze({ version: '{{version}}', platform: 'windows' }),
              configurable: true,
              writable: false,
              enumerable: true,
            });

            window.chrome.webview.addEventListener('message', function (e) {
              try {
                var payload = e.data;
                if (!payload || payload.kind !== 'vault') { return; }

                var objects = e.additionalObjects;
                var handle = objects && objects.length > 0 ? objects[0] : null;
                if (!handle) { return; }

                if (typeof window.__clinicShellDeliverVault === 'function') {
                  window.__clinicShellDeliverVault(handle);
                } else {
                  window.__clinicShellPendingVault = handle;
                }
              } catch (inner) { /* the coffre only — never the page's problem */ }
            });
          } catch (e) { /* the coffre only — never the page's problem */ }
        })();
        """;

    /// <summary>
    /// Hands the page the coffre folder, if this machine has one prepared and the loaded page is the server the
    /// shell is configured for.
    ///
    /// <para>⚠️ <b>Silent on every failure, and that is the contract</b> — a missing folder, an unplugged disk, or
    /// a WebView2 runtime predating the API all mean « no coffre on this machine », which the app already renders
    /// as a first-class state. None of them may interrupt a consultation, and none may stop the page loading.</para>
    /// </summary>
    public static void Deliver(CoreWebView2 core, ServerConfig config)
    {
        try
        {
            var path = VaultFolder.Prepare(ArchiveCopySettingsStore.Load());
            if (path.Length == 0)
            {
                return;
            }

            if (!IsExpectedOrigin(core.Source, config))
            {
                return;
            }

            var handle = core.Environment.CreateWebFileSystemDirectoryHandle(
                path, CoreWebView2FileSystemHandlePermission.ReadWrite);

            core.PostWebMessageAsJson("{\"kind\":\"vault\"}", new List<object> { handle });
        }
        catch
        {
            // An older evergreen runtime, a folder that vanished, a page that navigated away mid-call.
        }
    }

    /// <summary>
    /// Whether the loaded document is the server this shell was pointed at. Compared on <b>scheme, host and
    /// port</b> — the page's path is its own business, and a query string must not be able to change the answer.
    /// </summary>
    private static bool IsExpectedOrigin(string? source, ServerConfig config)
    {
        if (string.IsNullOrWhiteSpace(source) || !config.IsConfigured)
        {
            return false;
        }

        return Uri.TryCreate(source, UriKind.Absolute, out var loaded)
               && Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out var expected)
               && string.Equals(loaded.Scheme, expected.Scheme, StringComparison.OrdinalIgnoreCase)
               && string.Equals(loaded.Host, expected.Host, StringComparison.OrdinalIgnoreCase)
               && loaded.Port == expected.Port;
    }
}
