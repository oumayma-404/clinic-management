"use client"

import { useCallback, useEffect, useState } from "react"

import {
  chooseVault,
  currentVault,
  listenForShellVault,
  reconnectVault,
  shellCanDeliverVault,
  storedVaultExists,
  vaultSupported,
} from "@/lib/vault/handle"

/**
 * This machine's coffre, as a screen needs it.
 *
 * ⚠️ **Five states, and collapsing any two of them produces a wrong screen.** « This browser cannot hold a
 * coffre » (a phone, Safari) is not « you have not chosen a folder yet », and neither is « the shell will deliver
 * one shortly ». Only the middle ones are worth offering a button for; the first must explain instead of offering
 * a dead control, and `useUploadPolicy`'s contract applies here too — the server is the guard, this is a courtesy.
 *
 * ⚠️ `lapsed` was the state this hook was written for and missed. A browser drops the folder's grant when the
 * last tab for the origin closes, so the cabinet's first visit each morning found the folder *stored* and
 * *un-granted* — reported as `unpaired`, which sent the user back through the whole picker to re-navigate to a
 * folder the browser already knew. `reconnect` is one click, and it existed with no caller until this state did.
 */
export type VaultStatus =
  /** Still asking. Renders as neither present nor absent — a spinner, or nothing at all. */
  | "checking"
  /** A folder is reachable and writable right now. */
  | "ready"
  /** A folder is remembered but its permission has lapsed. One click restores it — offer `reconnect`. */
  | "lapsed"
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
      const lookup = await currentVault()
      if (cancelled) return

      if (lookup.kind === "ready") {
        setVault(lookup.handle)
        setStatus("ready")
        return
      }

      // Inside the shell a missing coffre is a configuration fact, not a browser limitation — the shell can
      // always deliver one — so it is « unpaired » even where `showDirectoryPicker` is the fallback path.
      const canHoldOne = vaultSupported() || shellCanDeliverVault()
      if (!canHoldOne) {
        setStatus("unsupported")
        return
      }

      setStatus(lookup.kind === "lapsed" ? "lapsed" : "unpaired")
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
      return
    }

    // Null covers two cases: the user refused the prompt (stay « lapsed », the button is worth pressing again)
    // and nothing is stored at all (fall through to the picker, or the button would be a dead control).
    const stored = await storedVaultExists()
    if (!stored) setStatus("unpaired")
  }, [])

  return { vault, status, pair, reconnect }
}
