using System;
using System.Collections.Generic;
using Microsoft.Web.WebView2.Core;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// The shell's bridge to the page — <b>its first</b>. Until the coffre this shell was indistinguishable from a
/// plain browser: it exposed no <c>window.__clinicShell</c> at all, which is why its version floor had to be read
/// over native HTTP before navigation instead of from the object every other client carries.
///
/// <para>It exposes two facts, three methods and one seam: <c>version</c>, <c>platform: "windows"</c>,
/// <c>confirmIdentity</c>, <c>relayHostFacts</c> + <c>installRelay</c> (the PC de secours, since 1.4), and the coffre
/// folder — and, since 1.8, the camera import (<see cref="Camera.CameraBridge"/>), whose batches arrive through a
/// second seam, <c>__clinicShellDeliverCamera</c>. <c>saveFile</c> and <c>print</c> are still absent — a WebView2 download works and the page's own
/// <c>window.print()</c> works, so each would solve a problem this shell does not have.</para>
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

            /*
             * The PC de secours (clinic-pc-copy, since 1.4). Same shape as confirmIdentity: a pending map in this
             * closure, a request id, and a deliver seam outside the frozen object, for the same AC-26 reason.
             *
             * ⚠️ Never rejects: a shell that cannot answer resolves `fallback` — null facts (the offer is not shown)
             * or a 'failed' outcome with a sentence. The install timeout is longer than the shell's own (a download
             * plus an installer), so the shell is always the one that speaks first.
             */
            var relayPending = {};
            var relayAlive = {};
            var relayCounter = 0;

            /*
             * `timeoutMs` counts from the last sign of life: a `camera-progress` naming the request re-arms it
             * (`relayAlive`), so a slow copy off a camera is never told « ne répond plus » while it is still copying.
             */
            function relayRequest(kind, body, fallback, timeoutMs) {
              return new Promise(function (resolve) {
                try {
                  var id = 'r' + (++relayCounter);
                  var settled = false;
                  var timer = null;
                  var settle = function (value) {
                    if (settled) { return; }
                    settled = true;
                    clearTimeout(timer);
                    delete relayPending[id];
                    delete relayAlive[id];
                    resolve(value);
                  };
                  var arm = function () {
                    clearTimeout(timer);
                    timer = setTimeout(function () { settle(fallback); }, timeoutMs);
                  };
                  relayPending[id] = settle;
                  relayAlive[id] = arm;
                  arm();
                  window.chrome.webview.postMessage(kind + ':' + id + (body === null ? '' : ':' + JSON.stringify(body)));
                } catch (e) {
                  resolve(fallback);
                }
              });
            }

            function relayHostFacts() {
              return relayRequest('relay-facts', null, null, 30000);
            }

            function installRelay(request) {
              var code = request && typeof request.code === 'string' ? request.code : '';
              var needBytes = request && typeof request.needBytes === 'number' ? request.needBytes : 0;
              return relayRequest('relay-install', { code: code, needBytes: needBytes }, {
                outcome: 'failed',
                sentence: "L'installation du PC de secours ne répond plus. « Paramètres → PC de secours » dira où elle en est."
              }, 7200000);
            }

            /*
             * AC-6.2 (since 1.5): while the cloud is locked for a silent PC de secours, does this device reach it, and
             * which box is it behind? Null when the shell cannot answer — the page then sends no report.
             */
            function relayProbe(request) {
              var body = {
                addresses: request && Array.isArray(request.addresses) ? request.addresses : [],
                port: request && typeof request.port === 'number' ? request.port : 0,
                fingerprint: request && typeof request.fingerprint === 'string' ? request.fingerprint : ''
              };
              return relayRequest('relay-probe', body, null, 20000);
            }

            /*
             * clinic-pc-copy Part 3 (since 1.6). `relayPrepare`: the cloud's ticket and where the PC is — the shell trades
             * it there and keeps the session in this WebView's cookie for the PC (true when it did). `relaySwitch`: the
             * cloud says the cabinet works on the PC — the shell asks the PC and moves there if it holds (true when it
             * moved). Both resolve false when the shell cannot.
             */
            function relayPrepare(request) {
              var body = {
                assertion: request && typeof request.assertion === 'string' ? request.assertion : '',
                relayId: request && typeof request.relayId === 'string' ? request.relayId : '',
                addresses: request && Array.isArray(request.addresses) ? request.addresses : [],
                port: request && typeof request.port === 'number' ? request.port : 0,
                fingerprint: request && typeof request.fingerprint === 'string' ? request.fingerprint : ''
              };
              return relayRequest('relay-prepare', body, false, 30000);
            }

            function relaySwitch() {
              return relayRequest('relay-switch', null, false, 20000);
            }

            /*
             * clinic-pc-copy D23 (since 1.6): the open form's state, kept by the shell for this window so it survives a
             * switch of server — `null` forgets it — and taken back once by the next clinic page.
             */
            function carryDraft(draft) {
              try {
                window.chrome.webview.postMessage('carry-draft:' + (typeof draft === 'string' ? draft : ''));
              } catch (e) { /* the form still works; it would not survive a switch */ }
            }

            function takeCarriedDraft() {
              return relayRequest('carry-take', null, null, 5000);
            }

            /*
             * staff-chat (since 1.7): a Windows notification and a flashing taskbar for a chat message, while this
             * window is not in front. `{ title, body, tag, urgent }` — `tag` is the conversation a click opens.
             */
            function notify(n) {
              try {
                window.chrome.webview.postMessage('notify:' + JSON.stringify({
                  title: n && n.title, body: n && n.body, tag: n && n.tag, urgent: !!(n && n.urgent)
                }));
              } catch (e) { /* the in-app notice and the tone still say it */ }
            }

            /*
             * camera-photo-import (since 1.8): the camera import. Requests ride `relayRequest` — a pending map in this
             * closure and a deliver seam outside the frozen object, AC-26's reason. Photos never come back as a REPLY:
             * file handles travel only with a posted message, so `cameraScan` answers a count while the batch reaches
             * `__clinicShellDeliverCamera`, or waits in `__clinicShellPendingCamera` until the page installs it.
             * Never rejects: a shell that cannot answer resolves the fallback.
             */
            function cameraStatus() {
              return relayRequest('camera-status', null, null, 10000);
            }

            function setCameraAutoImport(on) {
              return relayRequest('camera-auto', { on: !!on }, null, 10000);
            }

            // Fifteen minutes WITHOUT a `camera-progress` — a copy that is moving is never given up on.
            function cameraScan(request) {
              return relayRequest('camera-scan', {
                sourceKey: request && typeof request.sourceKey === 'string' ? request.sourceKey : undefined,
                includeOlder: !!(request && request.includeOlder)
              }, {
                delivered: 0, older: 0, failed: 0,
                sentence: "L'appareil ne répond plus depuis un quart d'heure. Débranchez-le, rebranchez-le, puis réessayez."
              }, 900000);
            }

            function cameraAck(ack) {
              try {
                window.chrome.webview.postMessage('camera-ack:' + JSON.stringify({
                  itemId: ack && ack.itemId,
                  outcome: ack && ack.outcome,
                  fileId: ack && ack.fileId,
                  patientId: ack && ack.patientId,
                  patientName: ack && ack.patientName,
                  keepOriginal: !!(ack && ack.keepOriginal)
                }));
              } catch (e) { /* the photo is filed; offered again, the server answers « déjà importée » */ }
            }

            function cameraOriginalRecorded(fileId) {
              try {
                window.chrome.webview.postMessage('camera-recorded:' + JSON.stringify({ fileId: fileId }));
              } catch (e) { /* re-sent on the next page load */ }
            }

            function ignoreCamera(sourceKey) {
              try {
                window.chrome.webview.postMessage('camera-ignore:' + JSON.stringify({ sourceKey: sourceKey }));
              } catch (e) { /* the notice comes back next time */ }
            }

            function openOriginal(fileId) {
              return relayRequest('camera-open', { fileId: fileId }, false, 15000);
            }

            function originalsHeld(fileIds) {
              return relayRequest('camera-held', { fileIds: Array.isArray(fileIds) ? fileIds.slice(0, 500) : [] }, [], 15000);
            }

            function copyOriginalsNow() {
              return relayRequest('camera-copy', null, null, 3600000);
            }

            window.__clinicShellDeliverRelayResult = function (id, value) {
              try {
                var settle = relayPending[id];
                if (typeof settle === 'function') { settle(value); }
              } catch (e) { /* the offer only — never the page's problem */ }
            };

            Object.defineProperty(window, '__clinicShell', {
              // ⚠️ The method set and the version move together — bridge.md's rule. `confirmIdentity` took this
              // shell from 1.2 to 1.3, `relayHostFacts` + `installRelay` from 1.3 to 1.4, `relayProbe` to 1.5,
              // `relayPrepare` + `relaySwitch` (and `carryDraft` + `takeCarriedDraft`) to 1.6, `notify` to 1.7,
              // the camera import (`cameraStatus` … `copyOriginalsNow`) to 1.8.
              value: Object.freeze({
                version: '{{version}}',
                platform: 'windows',
                confirmIdentity: confirmIdentity,
                relayHostFacts: relayHostFacts,
                installRelay: installRelay,
                relayProbe: relayProbe,
                relayPrepare: relayPrepare,
                relaySwitch: relaySwitch,
                carryDraft: carryDraft,
                takeCarriedDraft: takeCarriedDraft,
                notify: notify,
                cameraStatus: cameraStatus,
                setCameraAutoImport: setCameraAutoImport,
                cameraScan: cameraScan,
                cameraAck: cameraAck,
                cameraOriginalRecorded: cameraOriginalRecorded,
                ignoreCamera: ignoreCamera,
                openOriginal: openOriginal,
                originalsHeld: originalsHeld,
                copyOriginalsNow: copyOriginalsNow
              }),
              configurable: true,
              writable: false,
              enumerable: true,
            });

            window.chrome.webview.addEventListener('message', function (e) {
              try {
                var payload = e.data;
                if (!payload || typeof payload.kind !== 'string') { return; }
                var objects = e.additionalObjects;

                if (payload.kind === 'vault') {
                  var handle = objects && objects.length > 0 ? objects[0] : null;
                  if (!handle) { return; }

                  if (typeof window.__clinicShellDeliverVault === 'function') {
                    window.__clinicShellDeliverVault(handle);
                  } else {
                    window.__clinicShellPendingVault = handle;
                  }
                  return;
                }

                /*
                 * « Importer » is still copying: its wait starts again. Handed on to a page that listens (it may show
                 * the count), never queued — a stale count is worth nothing and would take a batch's place.
                 */
                if (payload.kind === 'camera-progress') {
                  var alive = relayAlive[payload.requestId];
                  if (typeof alive === 'function') { alive(); }
                  if (typeof window.__clinicShellDeliverCamera === 'function') {
                    window.__clinicShellDeliverCamera(payload, []);
                  }
                  return;
                }

                /*
                 * The camera's messages (since 1.8) — a batch with one read-only file handle per photo, a kept
                 * original, a copy to the archive disk. A QUEUE when the page is not listening yet, never the coffre's
                 * single slot: a second batch would overwrite the first.
                 */
                if (payload.kind.indexOf('camera-') === 0 || payload.kind === 'originals-copy') {
                  var handles = [];
                  if (objects) {
                    for (var i = 0; i < objects.length; i++) { handles.push(objects[i]); }
                  }

                  if (typeof window.__clinicShellDeliverCamera === 'function') {
                    window.__clinicShellDeliverCamera(payload, handles);
                  } else {
                    var queue = window.__clinicShellPendingCamera || (window.__clinicShellPendingCamera = []);
                    if (queue.length < 50) { queue.push({ payload: payload, handles: handles }); }
                  }
                }
              } catch (inner) { /* the coffre and the camera only — never the page's problem */ }
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
    public static bool IsExpectedOrigin(string? source, ServerConfig config)
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
