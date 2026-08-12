"use client"

import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Check, Copy, QrCode } from 'lucide-react'

interface TotpEnrolmentStepProps {
  /** `data:image/png;base64,…`, or null when the server could not render one. */
  qrPng: string | null
  /** The `otpauth://` URI — the tappable path on a phone, and the copy fallback. */
  secretUri: string | null
  /** The same secret in readable four-character groups. */
  secret: string | null
  /** Re-runs step one, for the « la préparation a échoué » path. */
  onRetry: () => void
  busy?: boolean
}

/**
 * What the user scans, taps or types to bind an authenticator
 * (`hosted-security-hardening` FR-1.3, step 18).
 *
 * ⚠️ **The QR sits on a fixed light plate at a stated minimum size, regardless of theme.** The app is
 * theme-aware; a QR painted on a dark card is unscannable, and the failure is silent — the camera simply never
 * locks on and the user concludes the feature is broken. `bg-white` and a fixed `size-48` are therefore
 * deliberate literals rather than tokens, and the plate keeps its quiet zone in both themes.
 *
 * ⚠️ **A failed render is shown as a failure with a retry, never as an empty box.** A missing image reads as
 * « still loading » forever. The readable secret below is a complete way in either way, so the retry is an
 * improvement rather than the only path.
 */
export function TotpEnrolmentStep({ qrPng, secretUri, secret, onRetry, busy }: TotpEnrolmentStepProps) {
  const [copied, setCopied] = useState<'secret' | 'uri' | null>(null)

  const copy = async (what: 'secret' | 'uri', value: string) => {
    try {
      await navigator.clipboard.writeText(value)
      setCopied(what)
      window.setTimeout(() => setCopied(null), 2000)
    } catch {
      // Clipboard access can be refused outright; the value is on screen and selectable either way.
    }
  }

  return (
    <div className="space-y-4">
      <p className="text-sm text-muted-foreground">
        Ouvrez votre application d&apos;authentification (Google Authenticator, Microsoft Authenticator, FreeOTP…)
        et ajoutez ce compte, puis saisissez le code affiché.
      </p>

      {qrPng ? (
        // Centred with `mx-auto` on the image rather than a `flex justify-center` row: § 11's rule, and the
        // `arch-clipping` check enforces it — centring inside a scroller pushes overflow to both ends and the
        // inline-start half leaves the scrollable region.
        /* eslint-disable-next-line @next/next/no-img-element -- a data: URI, not a remote asset */
        <img
          src={qrPng}
          alt="QR code à scanner avec votre application d'authentification"
          className="mx-auto block size-48 rounded-lg bg-white p-3"
        />
      ) : (
        <div
          role="status"
          className="space-y-3 rounded-lg border border-warning/30 bg-warning-wash p-3 text-sm text-warning-ink"
        >
          <p className="flex items-start gap-2">
            <QrCode aria-hidden className="mt-0.5 size-4 shrink-0" />
            <span>
              Le QR code n&apos;a pas pu être préparé. Vous pouvez tout de même ajouter le compte en saisissant la
              clé ci-dessous, ou réessayer.
            </span>
          </p>
          <Button type="button" variant="outline" onClick={onRetry} disabled={busy} className="min-h-11 w-full">
            Réessayer
          </Button>
        </div>
      )}

      {/* On a phone this opens the authenticator directly — the QR is for the case where the code is on
          another screen. */}
      {secretUri && (
        <Button asChild variant="outline" className="min-h-11 w-full">
          <a href={secretUri}>Ouvrir dans mon application d&apos;authentification</a>
        </Button>
      )}

      {secret && (
        <div className="space-y-2">
          <p className="text-sm font-medium">Ou saisissez cette clé manuellement</p>
          <div className="flex items-center gap-2">
            {/* Grouped in fours and `break-all`: it is read off a screen and typed into a phone, and an
                ungrouped 32-character run is where a transcription error comes from. */}
            <code className="min-w-0 flex-1 break-all rounded-md bg-muted/40 px-3 py-2 font-mono text-sm">
              {secret}
            </code>
            <Button
              type="button"
              variant="outline"
              size="icon"
              className="size-11 shrink-0"
              aria-label="Copier la clé"
              onClick={() => copy('secret', secret.replace(/\s/g, ''))}
            >
              {copied === 'secret' ? <Check aria-hidden className="size-4" /> : <Copy aria-hidden className="size-4" />}
            </Button>
          </div>
          {/* An inline async result announces itself. */}
          <p role="status" className="text-sm text-muted-foreground">
            {copied === 'secret' ? 'Clé copiée.' : ' '}
          </p>
        </div>
      )}
    </div>
  )
}
