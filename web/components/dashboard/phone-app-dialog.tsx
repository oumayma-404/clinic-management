"use client"

import { useEffect, useState } from "react"
import { Check, Loader2, Smartphone } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { metaApi, type PhoneAppDownloadDto } from "@/lib/api/meta"
import type { PhoneAppPersonDto } from "@/lib/api/types"
import { formatDateFr } from "@/lib/format"

const PLATFORM_LABELS: Record<string, string> = {
  android: "Android",
  "ios-home-screen": "iPhone",
  ios: "iPhone",
}

/**
 * « L'application sur votre téléphone » — opened from « Votre téléphone » in « Premiers pas » (guided-first-days B).
 *
 * <p>Android: a QR of the APK this server ships — the one the app's own updater is sent to, never the website's old
 * file. iPhone: nothing to install, three steps in Safari. Then « Déjà fait », one line per account, read from each
 * person's first sign-in from a phone app.</p>
 *
 * <p>⚠️ The QR sits on a fixed white plate at a fixed size, the authenticator QR's rule: on a dark card it is
 * unscannable and nothing says so.</p>
 */
export function PhoneAppDialog({
  open,
  onOpenChange,
  people,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  people: PhoneAppPersonDto[]
}) {
  const [download, setDownload] = useState<PhoneAppDownloadDto | null>(null)
  const [failed, setFailed] = useState(false)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    if (!open) return
    let cancelled = false
    setFailed(false)
    metaApi
      .phoneApp()
      .then((value) => {
        if (!cancelled) setDownload(value)
      })
      .catch(() => {
        if (!cancelled) setFailed(true)
      })
    return () => {
      cancelled = true
    }
  }, [open, attempt])

  // The address the iPhone opens: the one this page was reached on, which is the one known to work.
  const host = typeof window === "undefined" ? "" : window.location.host

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent mobile="sheet" className="md:max-w-xl">
        <DialogHeader>
          <DialogTitle>L&apos;application sur votre téléphone</DialogTitle>
          <DialogDescription>Pour vous et pour votre secrétaire, sur chaque téléphone.</DialogDescription>
        </DialogHeader>

        <DialogBody className="space-y-5">
          <section aria-labelledby="phone-android" className="space-y-3">
            <h3 id="phone-android" className="text-sm font-semibold">
              Android
            </h3>
            {failed ? (
              <LoadFailureNotice
                message="Le lien de l'application n'a pas pu être chargé."
                onRetry={() => setAttempt((n) => n + 1)}
              />
            ) : !download ? (
              <Loader2 className="size-5 animate-spin text-muted-foreground" aria-label="Chargement" />
            ) : download.androidUrl ? (
              <div className="flex flex-col gap-4 sm:flex-row sm:items-start">
                {download.androidQrPng && (
                  /* eslint-disable-next-line @next/next/no-img-element -- a data: URI, not a remote asset */
                  <img
                    src={download.androidQrPng}
                    alt="QR code de l'application Android"
                    className="mx-auto block size-40 shrink-0 rounded-lg bg-white p-2 sm:mx-0"
                  />
                )}
                <div className="min-w-0 space-y-2 text-sm">
                  <p>
                    Scannez avec l&apos;appareil photo, puis ouvrez le fichier. La première fois, Android demande
                    d&apos;autoriser l&apos;installation : acceptez.
                  </p>
                  <p className="text-muted-foreground">
                    Ou, depuis le téléphone :{" "}
                    <a href={download.androidUrl} className="font-medium text-primary underline">
                      télécharger l&apos;application
                    </a>
                  </p>
                </div>
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">
                L&apos;application Android n&apos;est pas proposée par ce serveur.
              </p>
            )}
          </section>

          <section aria-labelledby="phone-iphone" className="space-y-3">
            <h3 id="phone-iphone" className="text-sm font-semibold">
              iPhone
            </h3>
            <ol className="list-inside list-decimal space-y-1.5 text-sm">
              <li>
                Ouvrez <span className="font-medium break-all">{host}</span> dans Safari.
              </li>
              <li>Touchez « Partager ».</li>
              <li>Choisissez « Sur l&apos;écran d&apos;accueil ».</li>
            </ol>
          </section>

          {people.length > 0 && (
            <section aria-labelledby="phone-done" className="space-y-2">
              <h3 id="phone-done" className="text-sm font-semibold">
                Déjà fait
              </h3>
              <ul className="space-y-1.5 text-sm">
                {people.map((person) => (
                  <li key={`${person.name}-${person.role}`} className="flex items-center gap-2">
                    {person.platform ? (
                      <Check aria-hidden className="size-4 shrink-0 text-success" />
                    ) : (
                      <Smartphone aria-hidden className="size-4 shrink-0 text-muted-foreground" />
                    )}
                    <span className="min-w-0">
                      <span className="font-medium">{person.isYou ? "Vous" : person.name}</span>
                      {person.platform ? (
                        <>
                          {" · "}
                          {PLATFORM_LABELS[person.platform] ?? person.platform}
                          {person.firstSignInAt ? ` · ${formatDateFr(person.firstSignInAt)}` : ""}
                        </>
                      ) : (
                        <span className="text-muted-foreground"> · pas encore</span>
                      )}
                    </span>
                  </li>
                ))}
              </ul>
            </section>
          )}
        </DialogBody>

        <DialogFooter>
          <Button type="button" className="min-h-11" onClick={() => onOpenChange(false)}>
            Terminé
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
