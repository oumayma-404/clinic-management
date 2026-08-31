"use client"

import { useCallback, useEffect, useState } from "react"

import {
  chooseVault,
  currentVault,
  listenForShellVault,
  reconnectVault,
  shellCanDeliverVault,
  vaultSupported,
} from "@/lib/vault/handle"

/**
 * This machine's coffre, as a screen needs it.
 *
 * ⚠️ **Four states, and collapsing any two of them produces a wrong screen.** « This browser cannot hold a
 * coffre » (a phone, Safari) is not « you have not chosen a folder yet », and neither is « the shell will deliver
 * one shortly ». Only the second is worth offering a button for; the first must explain instead of offering a
 * dead control, and `useUploadPolicy`'s contract applies here too — the server is the guard, this is a courtesy.
 */
export type VaultStatus =
  /** Still asking. Renders as neither present nor absent — a spinner, or nothing at all. */
  | "checking"
  /** A folder is reachable and writable right now. */
  | "ready"
  /** This browser could hold one, but none is paired on this machine. A picker is worth offering. */
  | "unpaired"
  /** No File System Access API. Nothing to offer, and the reason is the browser. */
  | "unsupported"

export function useVault(): {
  vault: FileSystemDirectoryHandle | null
  status: VaultStatus
  /** Opens the folder picker. Requires a user gesture, so call it from a click and nowhere else. */
  pair: () => Promise<void>
  /** Re-asks for a folder already chosen whose permission lapsed. Also gesture-bound. */
  reconnect: () => Promise<void>
} {
  const [vault, setVault] = useState<FileSystemDirectoryHandle | null>(null)
  const [status, setStatus] = useState<VaultStatus>("checking")

  useEffect(() => {
    let cancelled = false

    // Installed before the first await: the shell may deliver while this effect is still resolving, and the seam
    // parks the handle rather than dropping it.
    listenForShellVault()

    void (async () => {
      const handle = await currentVault()
      if (cancelled) return

      if (handle) {
        setVault(handle)
        setStatus("ready")
        return
      }

      // Inside the shell a missing coffre is a configuration fact, not a browser limitation — the shell can
      // always deliver one — so it is « unpaired » even where `showDirectoryPicker` is the fallback path.
      setStatus(vaultSupported() || shellCanDeliverVault() ? "unpaired" : "unsupported")
    })()

    return () => {
      cancelled = true
    }
  }, [])

  const pair = useCallback(async () => {
    const handle = await chooseVault()
    if (handle) {
      setVault(handle)
      setStatus("ready")
    }
  }, [])

  const reconnect = useCallback(async () => {
    const handle = await reconnectVault()
    if (handle) {
      setVault(handle)
      setStatus("ready")
    }
  }, [])

  return { vault, status, pair, reconnect }
}
