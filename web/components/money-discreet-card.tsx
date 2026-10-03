"use client"

import { useEffect, useState } from "react"
import Link from "next/link"
import { toast } from "sonner"
import { Eye, EyeOff } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { StepUpDialog } from "@/components/security/step-up-dialog"
import { ApiError, ApiErrorCode } from "@/lib/api/client"
import { clinicsApi, SHOW_MONEY_STEP_UP_ACTION } from "@/lib/api/clinics"
import { securityApi } from "@/lib/api/security"
import { showErrorToast } from "@/lib/errors"
import { useMoneyVisibility } from "@/lib/money-visibility/money-visibility-context"
import { ZONES, zoneChipClass } from "@/lib/zones"

/** Whether this admin holds an authenticator. `unknown` on a failed read: the button stays live and the server decides. */
type TotpKnowledge = "unknown" | "enrolled" | "absent"

/**
 * « Mode discret » — the admin's one button that hides the cabinet's money screens on every device, and brings them
 * back with an authenticator code. Mounted at the bottom of Paramètres for an admin only (`POST /clinics/money/*` is
 * `AdminOnly`).
 *
 * <p>⚠️ Hiding is refused to an admin with no authenticator, because showing needs a code: the button is disabled
 * and names « Sécurité » rather than letting the press meet the refusal. A failed probe does NOT disable it —
 * « je n'ai pas pu lire » is not « vous n'en avez pas », and the server answers `totp_not_enrolled` either way.</p>
 */
export function MoneyDiscreetCard() {
  const { visibility, setVisibility } = useMoneyVisibility()
  const [totp, setTotp] = useState<TotpKnowledge>("unknown")
  const [busy, setBusy] = useState(false)
  const [stepUpOpen, setStepUpOpen] = useState(false)

  useEffect(() => {
    let cancelled = false
    securityApi
      .getTotpState()
      .then((state) => {
        if (!cancelled) setTotp(state.isEnrolled ? "enrolled" : "absent")
      })
      .catch(() => undefined)
    return () => {
      cancelled = true
    }
  }, [])

  const hidden = visibility === "hidden"
  const noAuthenticator = totp === "absent"

  const refuseIfNotEnrolled = (err: unknown): boolean => {
    if (err instanceof ApiError && err.code === ApiErrorCode.TotpNotEnrolled) {
      setTotp("absent")
      return true
    }
    return false
  }

  const hide = async () => {
    setBusy(true)
    try {
      const result = await clinicsApi.hideMoney()
      setVisibility(result.isMoneyHidden ? "hidden" : "shown")
      toast.success("Mode discret activé.")
    } catch (err) {
      if (!refuseIfNotEnrolled(err)) showErrorToast(err, "Le mode discret n'a pas pu être activé.")
    } finally {
      setBusy(false)
    }
  }

  const show = async (confirmationToken: string) => {
    setBusy(true)
    try {
      const result = await clinicsApi.showMoney(confirmationToken)
      setVisibility(result.isMoneyHidden ? "hidden" : "shown")
      toast.success("Mode discret désactivé.")
    } catch (err) {
      if (!refuseIfNotEnrolled(err)) showErrorToast(err, "Le mode discret n'a pas pu être désactivé.")
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <CardContent className="flex flex-wrap items-center justify-between gap-3 p-4">
        {/* No description, on the owner's call. The no-authenticator line stays: it is a refusal, not a caption. */}
        <div className="flex min-w-0 basis-40 grow items-center gap-2.5">
          <span
            aria-hidden="true"
            className={`flex size-8 shrink-0 items-center justify-center rounded-lg ${zoneChipClass(ZONES.config)}`}
          >
            {hidden ? <EyeOff className="size-4" strokeWidth={1.75} /> : <Eye className="size-4" strokeWidth={1.75} />}
          </span>
          <div className="min-w-0 space-y-0.5">
            <p className="text-sm font-medium">Mode discret</p>
            {noAuthenticator && (
              <p role="note" className="text-xs text-muted-foreground">
                Activez l&apos;authentificateur dans{" "}
                <Link href="/securite" className="font-medium text-primary underline underline-offset-2">
                  «&nbsp;Sécurité&nbsp;»
                </Link>
                .
              </p>
            )}
          </div>
        </div>

        <Button
          variant={hidden ? "default" : "outline"}
          size="sm"
          className="w-full coarse:min-h-11 sm:w-auto"
          disabled={busy || visibility === "unknown" || noAuthenticator}
          onClick={hidden ? () => setStepUpOpen(true) : () => void hide()}
        >
          {hidden ? <Eye className="me-1.5 size-4" /> : <EyeOff className="me-1.5 size-4" />}
          {hidden ? "Afficher" : "Masquer"}
        </Button>
      </CardContent>

      {/* Code only: the server refuses a password for this action, so the dialog never offers one. */}
      <StepUpDialog
        open={stepUpOpen}
        onOpenChange={setStepUpOpen}
        action={SHOW_MONEY_STEP_UP_ACTION}
        purpose="Désactiver le mode discret."
        hasTotp
        onConfirmed={(confirmationToken) => void show(confirmationToken)}
      />
    </Card>
  )
}
