"use client"

import { useCallback, useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { securityApi, type SessionDevice } from '@/lib/api/security'
import { showErrorToast } from '@/lib/errors'
import { formatDateTime, quoteFr } from '@/lib/format'
import { Laptop, ShieldCheck } from 'lucide-react'
import { toast } from 'sonner'

/**
 * « Mes appareils » — the sessions this account has open, and the one control that ends one.
 *
 * <p><b>Why this ships with « Rester connecté sur cet appareil » and not after it.</b> A 30-day credential on a
 * device you cannot enumerate and cannot revoke is not a convenience, it is a hole: a lost laptop would mean a
 * month of access with the only lever being a password change — which bumps <code>TokenVersion</code> and
 * therefore signs the owner out of every device including the one in their hand.</p>
 *
 * <p>⚠️ <b>A card list at every width, with no table.</b> A row is a name, a date and one action, so there is
 * nothing to compare down a column and the two-tree hinge § 6 exists for would buy nothing — the same reasoning
 * <code>/fichiers</code> records. It is a real <code>&lt;ul&gt;</code> rather than a stack of divs so the count
 * is announced.</p>
 */
export function MyDevicesCard() {
  const [devices, setDevices] = useState<SessionDevice[] | null>(null)
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)
  const [confirming, setConfirming] = useState<SessionDevice | null>(null)
  const [endingId, setEndingId] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    setFailed(false)
    try {
      setDevices(await securityApi.listSessions())
    } catch {
      // ⚠️ A failed read is NOT « aucun appareil ». They are opposite facts with the same picture, and here the
      // wrong one is actively reassuring: it would tell somebody checking after a theft that nothing is open.
      setFailed(true)
      setDevices(null)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const endSession = async (device: SessionDevice) => {
    setEndingId(device.id)
    try {
      await securityApi.endSession(device.id)
      toast.success('Appareil déconnecté.')
      setConfirming(null)

      // Ending your own session leaves this page holding a credential the server has just revoked. Reloading is
      // what turns that into an ordinary trip to /login instead of every later call failing one at a time.
      if (device.isCurrent) {
        window.location.href = '/login'
        return
      }

      await load()
    } catch (err) {
      showErrorToast(err)
    } finally {
      setEndingId(null)
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Mes appareils</CardTitle>
        <CardDescription>
          Les sessions actuellement ouvertes sur votre compte. Déconnectez tout appareil que vous ne reconnaissez
          pas ou que vous n&apos;avez plus.
        </CardDescription>
      </CardHeader>

      <CardContent className="space-y-4">
        {loading && <p className="text-sm text-muted-foreground">Chargement…</p>}

        {/* § 13: a failed read gets a retry banner and announces itself, never an empty list. */}
        {!loading && failed && (
          <div role="alert" className="space-y-3 rounded-lg border border-border bg-muted/40 p-3">
            <p className="text-sm">La liste de vos appareils n&apos;a pas pu être chargée.</p>
            <Button onClick={load} variant="outline" className="min-h-11">
              Réessayer
            </Button>
          </div>
        )}

        {!loading && !failed && devices?.length === 0 && (
          <p className="text-sm text-muted-foreground">Aucune session ouverte.</p>
        )}

        {!loading && !failed && devices && devices.length > 0 && (
          <ul className="divide-y divide-border rounded-lg border border-border">
            {devices.map((device) => (
              <li key={device.id} className="flex flex-wrap items-start gap-3 p-3 sm:flex-nowrap">
                <Laptop className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden />

                <div className="min-w-0 flex-1 space-y-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-medium">{device.deviceLabel || 'Appareil sans nom'}</span>
                    {device.isCurrent && <Badge variant="secondary">Cet appareil</Badge>}
                    {device.isTrusted && (
                      <Badge variant="outline" className="gap-1">
                        <ShieldCheck className="size-3" aria-hidden />
                        Connexion prolongée
                      </Badge>
                    )}
                  </div>

                  {/* ⚠️ « Dernière activité », never « dernière utilisation ». An open tab renews its own
                      credential roughly every half hour, so this advances while nobody is at the machine — the
                      stronger word would tell a user their unattended reception PC was being used. */}
                  <p className="text-xs text-muted-foreground">
                    Dernière activité : {formatDateTime(device.lastActiveAtUtc)}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    Connecté depuis le {formatDateTime(device.createdAtUtc)}
                  </p>
                </div>

                <Button
                  variant="outline"
                  className="min-h-11 w-full shrink-0 sm:w-auto"
                  onClick={() => setConfirming(device)}
                  disabled={endingId !== null}
                >
                  Déconnecter
                </Button>
              </li>
            ))}
          </ul>
        )}
      </CardContent>

      {/* § 13: a destructive confirm names what it destroys — with several unnamed devices open, « Êtes-vous
          sûr ? » cannot say which one is about to go. */}
      <AlertDialog open={confirming !== null} onOpenChange={(open) => !open && setConfirming(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              Déconnecter {quoteFr(confirming?.deviceLabel || 'cet appareil')} ?
            </AlertDialogTitle>
            <AlertDialogDescription>
              {confirming?.isCurrent
                ? "C'est l'appareil que vous utilisez en ce moment : vous serez ramené à l'écran de connexion."
                : 'Cet appareil devra se reconnecter avec le mot de passe et le code de vérification. Vos autres appareils restent connectés.'}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel className="min-h-11">Annuler</AlertDialogCancel>
            <AlertDialogAction
              className="min-h-11"
              disabled={endingId !== null}
              onClick={(event) => {
                // The dialog closes on its own action click; we drive it from the request's outcome instead, so
                // a failure leaves the confirmation open with its message rather than vanishing silently.
                event.preventDefault()
                if (confirming) void endSession(confirming)
              }}
            >
              {endingId !== null ? 'Déconnexion…' : 'Déconnecter'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Card>
  )
}
