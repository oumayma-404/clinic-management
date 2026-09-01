using System;
using System.Collections.Generic;
using Microsoft.Web.WebView2.Core;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// The shell's bridge to the page — <b>its first</b>. Until the coffre this shell was indistinguishable from a
/// plain browser: it exposed no <c>window.__clinicShell</c> at all, which is why its version floor had to be read
/// over native HTTP before navigation instead of from the object every other client carries.
///
/// <para>It exposes two facts, one method and one seam: <c>version</c>, <c>platform: "windows"</c>,
/// <c>confirmIdentity</c>, and the coffre folder. <c>saveFile</c> and <c>print</c> are still absent — a WebView2
/// download works and the page's own <c>window.print()</c> works, so each would solve a problem this shell does
/// not have.</para>
///
/// <para>⚠️ <b><c>confirmIdentity</c> was absent for the opposite reason, and its absence was a defect rather
/// than a decision.</b> The note here used to say this shell had « no biometric prompt » alongside the two
/// members it genuinely does not need. But the problem that method solves — the inactivity limit ending a
/// session outright instead of pausing it — is one this shell has in full, and worse than the phones do: a phone
/// is already behind its own lock screen, while a dentist's PC sits unlocked in a treatment room with the app
/// open through a forty-minute appointment. So the phone locked and resumed to the same open fiche while the
/// desktop demanded a password and a six-digit code from an authenticator across the room. Windows Hello is the
/// same question the phones ask, and the web bundle's contract is unchanged.</para>
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

            /*
             * The identity resolvers, and the reason they live in this closure rather than on `__clinicShell`.
             *
             * AC-26 verifies the bridge by DELETING `window.__clinicShell` at runtime. A resolver map living on
             * that object would either die with it — stranding a promise the lock gate is awaiting, leaving an
             * opaque overlay over the app for ever — or outlive it holding a live reference. Same shape, and the
             * same reason, as `__clinicShellDeliverVault` above.
             */
            var identityPending = {};
            var identityCounter = 0;

            /*
             * ⚠️ **Never rejects and never throws** (bridge.md). The single call site is written not to fail
             * open, so every failure is a VALUE: a shell that cannot ask answers 'unavailable' and the web
             * bundle falls straight through to the password screen.
             *
             * ⚠️ The timeout is not a nicety. If the native side never answers — a crash mid-prompt, a window
             * that went away — the promise would never settle and `<SessionLockGate>` would sit over a mounted
             * app with no control that does anything. Two minutes is longer than any real Hello interaction
             * (the OS gives the user its own generous window) and short enough that a wedged shell resolves to
             * the password screen rather than to nothing at all.
             */
            function confirmIdentity() {
              return new Promise(function (resolve) {
                try {
                  var id = 'i' + (++identityCounter);
                  var settled = false;

                  var settle = function (outcome) {
                    if (settled) { return; }
                    settled = true;
                    delete identityPending[id];
                    resolve(outcome);
                  };

                  identityPending[id] = settle;
                  setTimeout(function () { settle('unavailable'); }, 120000);

                  window.chrome.webview.postMessage('identity:' + id);
                } catch (e) {
                  resolve('unavailable');
                }
              });
            }

            window.__clinicShellDeliverIdentityResult = function (id, outcome) {
              try {
                var settle = identityPending[id];
                if (typeof settle === 'function') { settle(outcome); }
              } catch (e) { /* the lock gate only — never the page's problem */ }
            };

            Object.defineProperty(window, '__clinicShell', {
              // ⚠️ The method set and the version move together — bridge.md's rule. `confirmIdentity` arriving
              // here is what took this shell from 1.2 to 1.3.
              value: Object.freeze({ version: '{{version}}', platform: 'windows', confirmIdentity: confirmIdentity }),
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
