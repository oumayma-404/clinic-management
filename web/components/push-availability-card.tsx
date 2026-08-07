"use client"

import { useCallback, useEffect, useState } from "react"
import { BellRing, CheckCircle2, Info, Smartphone } from "lucide-react"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { ZONES, zoneChipClass } from "@/lib/zones"
import { pushDevicesApi, type PushAvailability } from "@/lib/api/push-devices"
import { showErrorToast } from "@/lib/errors"
import { cn } from "@/lib/utils"

/**
 * Admin-only « Notifications système » card (`mobile-native-shells` Part 6, AC-51 / AC-52).
 *
 * <p><b>Why a card and not a toggle.</b> Nothing here is editable: there is one mobile app per deployment, so one
 * Firebase project and one Apple team, and a clinic cannot switch push on for itself. What the owner needs is the
 * answer to « does the app on my phone actually get notifications? », which before this had no home at all.</p>
 *
 * <p><b>Per platform, and every platform always listed.</b> AC-52's requirement is that a half-configured install
 * must not read as a working one — so « iOS : non configuré » is printed rather than omitted. An absent row is not
 * a statement, the same reasoning la caisse's zero-valued per-method figures follow.</p>
 *
 * <p>The verdict and its French explanation both come from the server, so this screen, the registration refusal a
 * shell sees and the reason a blocked send records are one wording rather than three.</p>
 */
export function PushAvailabilityCard() {
  const [availability, setAvailability] = useState<PushAvailability | null>(null)
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    setFailed(false)
    try {
      setAvailability(await pushDevicesApi.availability())
    } catch (error) {
      // A failed read is not « push is off » — that would be a confidently wrong answer about whether staff are
      // being reached. It gets its own retry state.
      setFailed(true)
      showErrorToast(error)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="flex min-h-9 items-center gap-2 text-base">
          <span className={cn("flex size-8 shrink-0 items-center justify-center rounded-lg", zoneChipClass(ZONES.config))}>
            <BellRing className="size-4" />
          </span>
          Notifications système
        </CardTitle>
      </CardHeader>

      <CardContent className="space-y-3">
        {loading && (
          <p className="text-sm text-muted-foreground" role="status">
            Chargement…
          </p>
        )}

        {!loading && failed && (
          <div className="flex flex-wrap items-center gap-2">
            <p className="text-sm text-muted-foreground">
              L&apos;état des notifications système n&apos;a pas pu être chargé.
            </p>
            <Button variant="outline" size="sm" className="coarse:h-11" onClick={() => void load()}>
              Réessayer
            </Button>
          </div>
        )}

        {!loading && !failed && availability && (
          <>
            {/* Stated once, plainly, before the per-platform rows: AC-51 asks that an installation with no push
                at all say so, and « les deux plateformes sont non configurées » is a conclusion nobody should
                have to assemble from two rows. */}
            <p className="text-sm text-muted-foreground">
              {availability.availableAtAll
                ? "L’application mobile peut envoyer des notifications sur l’écran verrouillé des appareils enregistrés."
                : "Les notifications système ne sont pas disponibles sur cette installation. L’application reste entièrement utilisable et le centre de notifications de l’application n’est pas affecté."}
            </p>

            <ul className="space-y-2">
              {availability.platforms.map((platform) => (
                <li
                  key={platform.platform}
                  className="flex flex-wrap items-start gap-2 rounded-lg border border-border bg-muted/40 p-3"
                >
                  <Smartphone className="mt-0.5 size-4 shrink-0 text-muted-foreground" />
                  <div className="min-w-0 flex-1 space-y-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="text-sm font-medium">{platform.label}</span>
                      {platform.supported ? (
                        <Badge className="bg-success-wash text-success">
                          <CheckCircle2 className="me-1 size-3" />
                          Disponible
                        </Badge>
                      ) : (
                        <Badge variant="outline" className="text-muted-foreground">
                          Non configuré
                        </Badge>
                      )}
                    </div>

                    {/* The server's own wording. A « non configuré » badge alone does not tell an operator
                        whether to add credentials or whether this topology simply cannot push. */}
                    {platform.reason && (
                      <p className="text-sm text-muted-foreground">{platform.reason}</p>
                    )}

                    <p className="text-2xs text-muted-foreground">
                      {platform.registeredDevices === 0
                        ? "Aucun appareil enregistré"
                        : `${platform.registeredDevices} appareil${platform.registeredDevices > 1 ? "s" : ""} enregistré${platform.registeredDevices > 1 ? "s" : ""}`}
                    </p>
                  </div>
                </li>
              ))}
            </ul>

            <div className="flex items-start gap-2 rounded-lg border border-primary/25 bg-accent/20 p-3">
              <Info className="mt-0.5 size-4 shrink-0 text-primary" />
              <p className="text-sm text-muted-foreground">
                Seuls les rendez-vous et les comptes rendus de visite déclenchent une notification système. Le stock
                faible, les péremptions, les sauvegardes et les rappels non envoyés restent dans le centre de
                notifications de l&apos;application.
              </p>
            </div>
          </>
        )}
      </CardContent>
    </Card>
  )
}
