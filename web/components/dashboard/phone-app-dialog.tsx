"use client"

import { useEffect, useState } from "react"
import { Check, ExternalLink, Loader2, Smartphone } from "lucide-react"

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
import { metaApi, type PhoneAppDownloadDto } from "@/lib/api/meta"
import type { PhoneAppPersonDto } from "@/lib/api/types"
import { formatDateFr } from "@/lib/format"
import { APEXA_INSTALL_PAGE } from "@/lib/onboarding-videos"

const PLATFORM_LABELS: Record<string, string> = {
  android: "Android",
  "ios-home-screen": "iPhone",
  ios: "iPhone",
}

/**
 * « L'application sur votre téléphone » — opened from « Votre téléphone » in « Premiers pas » (guided-first-days B).
 *
 * <p>One way in, for Android and iPhone alike: the website's « Installez APEXA » section — a QR code to scan from
 * the computer, and the same page as a button. Then « Déjà fait », one line per account, read from each person's
 * first sign-in from a phone app.</p>
 *
 * <p>⚠️ It used to show this server's own APK as a QR and, on a server shipping none, « L'application Android n'est
 * pas proposée par ce serveur » — read as « APEXA has no phone app » (owner's call, 2026-11-04: send them to the
 * site, which is also what the welcome e-mail and the invitation link to).</p>
 *
 * <p>⚠️ The QR sits on a fixed white plate at a fixed size, the authenticator QR's rule: on a dark card it is
 * unscannable and nothing says so. It is withheld below `sm:` — on the phone itself the button is the way.</p>
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
        // The button below needs no read; only the QR is lost, so nothing is said beyond its absence.
        if (!cancelled) setFailed(true)
      })
    return () => {
      cancelled = true
    }
  }, [open])

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent mobile="sheet" className="md:max-w-lg">
        <DialogHeader>
          <DialogTitle>L&apos;application sur votre téléphone</DialogTitle>
          <DialogDescription>Android et iPhone, pour vous et pour votre secrétaire.</DialogDescription>
        </DialogHeader>

        <DialogBody className="space-y-5">
          <section aria-label="Télécharger l'application" className="flex flex-col items-center gap-4 sm:flex-row sm:items-center">
            <div className="hidden shrink-0 sm:block">
              {download ? (
                /* eslint-disable-next-line @next/next/no-img-element -- a data: URI, not a remote asset */
                <img
                  src={download.qrPng}
                  alt="QR code de la page de téléchargement d'APEXA"
                  className="block size-40 rounded-lg bg-white p-2"
                />
              ) : failed ? null : (
                <div className="grid size-40 place-items-center rounded-lg border">
                  <Loader2 className="size-5 animate-spin text-muted-foreground" aria-label="Chargement" />
                </div>
              )}
            </div>
            <div className="flex w-full min-w-0 flex-col gap-3 sm:w-auto">
              <p className="hidden text-sm sm:block">Scannez ce code avec l&apos;appareil photo du téléphone.</p>
              <Button asChild className="min-h-11 w-full sm:w-auto">
                <a href={APEXA_INSTALL_PAGE} target="_blank" rel="noopener noreferrer">
                  <ExternalLink aria-hidden />
                  Ouvrir la page de téléchargement
                  <span className="sr-only"> (apexa.tn, s&apos;ouvre dans un nouvel onglet)</span>
                </a>
              </Button>
            </div>
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
          <Button type="button" variant="outline" className="min-h-11" onClick={() => onOpenChange(false)}>
            Terminé
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
