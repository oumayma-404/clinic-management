/*
 * APEXA's service worker — phone and browser notifications, and nothing else (staff-chat Part 8).
 *
 * ⚠️ It has no `fetch` handler, deliberately: it caches nothing and never stands between the app and the server, so a
 * new deployment cannot be hidden behind a stale copy of the old one.
 *
 * A push carries the fixed French label and the routing — never a message's text (AC-47) — so the lock screen reads
 * « Nouveau message ». When the page has already shown a notice for that thread (it says who wrote and what), that
 * notice is kept rather than replaced by the poorer one.
 */

self.addEventListener("install", () => self.skipWaiting())

self.addEventListener("activate", (event) => event.waitUntil(self.clients.claim()))

self.addEventListener("push", (event) => {
  let data = {}
  try {
    data = event.data ? event.data.json() : {}
  } catch (e) {
    data = {}
  }
  const tag = typeof data.tag === "string" && data.tag ? data.tag : "apexa"
  const routing = { conversation: data.conversation || null, appointmentId: data.appointmentId || null }
  const plain = () => self.registration.showNotification(data.title || "APEXA", { tag, icon: "/icon-192.png", data: routing })

  // Every push must show a notification — iOS withdraws a subscription whose pushes show nothing — so no step here may
  // stop it: an engine without getNotifications, or a failed re-show, falls back to the plain one.
  event.waitUntil(
    shownWithTag(tag)
      .then((shown) => {
        const kept = shown[0]
        if (!kept) return plain()
        // The page already said who wrote and what: keep that richer notice rather than the bare label.
        return self.registration.showNotification(kept.title, {
          body: kept.body,
          tag,
          icon: kept.icon || "/icon-192.png",
          data: kept.data || routing,
        })
      })
      .catch(plain),
  )
})

function shownWithTag(tag) {
  try {
    return Promise.resolve(self.registration.getNotifications({ tag })).catch(() => [])
  } catch (e) {
    return Promise.resolve([])
  }
}

self.addEventListener("notificationclick", (event) => {
  event.notification.close()
  const data = event.notification.data || {}
  const conversation = typeof data.conversation === "string" ? data.conversation : null
  const appointmentId = typeof data.appointmentId === "string" ? data.appointmentId : null
  const url = conversation
    ? "/?chat=" + encodeURIComponent(conversation)
    : appointmentId
      ? "/appointments?appointmentId=" + encodeURIComponent(appointmentId)
      : "/"

  event.waitUntil(
    self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((windows) => {
      const open = windows[0]
      if (!open) return self.clients.openWindow(url)
      return open.focus().then((client) => {
        const target = client || open
        if (conversation) target.postMessage({ type: "apexa-open-staff-chat", conversation })
        else target.postMessage({ type: "apexa-open-url", url })
      })
    }),
  )
})
