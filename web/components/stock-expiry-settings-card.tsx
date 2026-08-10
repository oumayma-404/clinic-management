"use client"

import { useEffect, useState } from "react"
import { Hourglass, Loader2 } from "lucide-react"
import { toast } from "sonner"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { stockApi } from "@/lib/api/stock"
import { showErrorToast } from "@/lib/errors"

/**
 * « Alerte de péremption » — how many days of warning before a lot expires (AC-20).
 *
 * <p><b>Why this card exists at all.</b> `Clinic.SetStockExpiryLeadDays`, its column and both server readers
 * shipped together and correct, with **nothing able to reach them**: every clinic ran on the 30-day default for
 * the life of the product, and a practice that stocks nothing perishable had a daily notification it could not
 * silence. That is this repo's recurring shape — a setting that ships without a caller — and it is also why the
 * range guard being wrong (`1–365`, refusing the one value that means "off") went unnoticed for just as long.</p>
 *
 * <p>⚠️ <b>Zero is a real setting, not an empty field.</b> The helper text says so and the summary line below the
 * input restates it, because « 0 jour » reads naturally as « prévenez-moi le jour même » — the opposite of what
 * both readers do with it.</p>
 *
 * <p>Admin-only, mirroring the server: the read is `AnyClinicRole` (the stock list's « expire bientôt » column is
 * computed from this, so anyone looking at that column may see the window behind it) while the write is
 * `AdminOnly`, like the recall interval it is modelled on.</p>
 */
export function StockExpirySettingsCard({ isAdmin, onChanged }: { isAdmin: boolean; onChanged?: () => void }) {
  const [leadDays, setLeadDays] = useState<string>("")
  const [saved, setSaved] = useState<number | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [failed, setFailed] = useState(false)

  const load = async () => {
    setLoading(true)
    try {
      const settings = await stockApi.getExpirySettings()
      setSaved(settings.leadDays)
      setLeadDays(String(settings.leadDays))
      setFailed(false)
    } catch {
      // A failed read must not render as « 0 jour », which would say the alert is off when we simply do not know.
      setFailed(true)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void load()
  }, [])

  const parsed = Number(leadDays)
  const isValid = leadDays.trim() !== "" && Number.isInteger(parsed) && parsed >= 0 && parsed <= 365
  const isDirty = saved !== null && parsed !== saved

  const handleSave = async () => {
    if (!isValid || saving) return
    setSaving(true)
    try {
      const result = await stockApi.setExpirySettings(parsed)
      setSaved(result.leadDays)
      setLeadDays(String(result.leadDays))
      toast.success(
        result.leadDays === 0
          ? "Alerte de péremption désactivée."
          : `Alerte de péremption réglée sur ${result.leadDays} jours.`,
      )
      onChanged?.()
    } catch (err) {
      showErrorToast(err)
    } finally {
      setSaving(false)
    }
  }

  return (
    <Card>
      <CardHeader>
        <div className="flex items-center gap-3">
          <div className="flex size-8 items-center justify-center rounded-lg bg-warning-wash">
            <Hourglass className="size-4 text-warning-ink" aria-hidden="true" />
          </div>
          <div>
            <CardTitle>Alerte de péremption</CardTitle>
            <CardDescription>
              Combien de jours à l&apos;avance un lot proche de sa date doit être signalé.
            </CardDescription>
          </div>
        </div>
      </CardHeader>
      <CardContent>
        {failed ? (
          <div className="flex flex-col items-start gap-3">
            <p className="text-sm text-muted-foreground">Le réglage n&apos;a pas pu être chargé.</p>
            <Button variant="outline" onClick={() => void load()}>
              Réessayer
            </Button>
          </div>
        ) : loading ? (
          <div className="h-10 w-full max-w-xs animate-pulse rounded-md bg-muted/60" />
        ) : (
          <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
            <div className="space-y-1.5">
              <Label htmlFor="stock-expiry-lead-days">Jours d&apos;avance</Label>
              <Input
                id="stock-expiry-lead-days"
                type="number"
                min={0}
                max={365}
                inputMode="numeric"
                className="w-full sm:w-40"
                value={leadDays}
                disabled={!isAdmin || saving}
                onChange={(e) => setLeadDays(e.target.value)}
              />
            </div>
            {isAdmin && (
              <Button onClick={handleSave} disabled={!isValid || !isDirty || saving} className="coarse:h-11">
                {saving && <Loader2 className="size-4 animate-spin" aria-hidden="true" />}
                Enregistrer
              </Button>
            )}
          </div>
        )}

        {!failed && !loading && (
          <p className="mt-3 text-sm text-muted-foreground" role="status">
            {!isValid
              ? "Indiquez un nombre de jours entre 0 et 365."
              : parsed === 0
                ? "Alerte désactivée : aucun lot ne sera signalé comme « expire bientôt »."
                : `Un lot est signalé ${parsed} jour${parsed > 1 ? "s" : ""} avant sa date de péremption. Mettez 0 pour désactiver l'alerte.`}
          </p>
        )}

        {!isAdmin && !failed && !loading && (
          <p className="mt-1 text-sm text-muted-foreground">
            Seul un administrateur peut modifier ce réglage.
          </p>
        )}
      </CardContent>
    </Card>
  )
}
