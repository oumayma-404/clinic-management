"use client";

import { useRouter } from "next/navigation";
import { useId, useState } from "react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet";
import type { PlatformMessagingRecorded } from "@/lib/api/platform";

/**
 * « Enregistrer un forfait de rappels » (`vendor-whatsapp-messaging-quota` US-6).
 *
 * ⚠️ **The vendor chooses a KIND, never a month for a standing forfait** (AC-6.4a). Which month a monthly figure takes
 * effect in is the server's decision — immediately if it raises, next month if it lowers — so this form deliberately
 * offers no month field in that mode, and says the rule out loud instead. Offering one would be offering a way to cut a
 * practice off mid-afternoon by a change it had no warning of.
 *
 * ⚠️ **Full screen below `lg:`, a dialog above it, and the primary action is pinned in both** — `RecordPaymentSheet`'s
 * arrangement and its reasons: the body scrolls inside `flex-1`, the footer is a `shrink-0` sibling, so « Enregistrer »
 * survives the on-screen keyboard at every height including a 380 px landscape one. `dvh`, never `vh`.
 *
 * ⚠️ **One idempotency key per opened sheet** (AC-6.7). Minted when the sheet opens and reused for every submission from
 * it, so the second tap of a double-click carries the first tap's key and the server answers with the first outcome.
 * Re-minting per submit defeats the mechanism; minting once per mount makes a *deliberate* second allocation impossible.
 *
 * ⚠️ **The refusal shown is the server's own sentence.** Nothing here rewords one or infers it from a status.
 */
export function RecordAllowanceSheet({
  clinicId,
  clinicName,
  monthLabel,
  standingAllowance,
}: {
  clinicId: string;
  clinicName: string;
  monthLabel: string;
  /** What the cabinet gets every month today, or null where nothing reaches this month — never rendered as 0. */
  standingAllowance: number | null;
}) {
  const router = useRouter();
  const fieldId = useId();

  const [open, setOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [recorded, setRecorded] = useState<PlatformMessagingRecorded | null>(null);
  const [idempotencyKey, setIdempotencyKey] = useState("");
  const [dirty, setDirty] = useState(false);

  const [kind, setKind] = useState<"standing" | "topUp">("standing");
  const [messages, setMessages] = useState("");
  const [month, setMonth] = useState("");
  const [complimentary, setComplimentary] = useState(false);
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState("Transfer");
  const [reference, setReference] = useState("");
  const [note, setNote] = useState("");

  function openChanged(next: boolean) {
    if (next) {
      // A fresh key per opening: two allocations genuinely recorded one after the other must both land (EC-5), and one
      // key for the lifetime of the page would silently replay the first.
      setIdempotencyKey(crypto.randomUUID());
      setError(null);
      setRecorded(null);
      setDirty(false);
      setOpen(true);
      return;
    }

    if (submitting) {
      return;
    }

    // Escape, the close control and the overlay tap all arrive here, so the confirmation covers every way out.
    if (dirty && recorded === null
      && !window.confirm("Abandonner ce forfait ? Les valeurs saisies seront perdues.")) {
      return;
    }

    setOpen(false);
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);

    const count = Number(messages);

    try {
      const response = await fetch("/bff/forfaits", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          clinicId,
          idempotencyKey,
          // Exactly one of the two, and no month on the standing form — the server refuses both spellings, and sending
          // a month it will refuse would turn a correct submission into a puzzling error.
          messagesPerMonth: kind === "standing" ? count : null,
          topUpMessages: kind === "topUp" ? count : null,
          appliesToMonth: kind === "topUp" ? month : null,
          // « Offert » is NO amount, never 0 — the server refuses an amount of zero outright (AC-6.6).
          amountDt: complimentary || amount.trim() === "" ? null : Number(amount.replace(",", ".")),
          method: complimentary ? null : method,
          reference: reference.trim() === "" ? null : reference.trim(),
          note: note.trim() === "" ? null : note.trim(),
        }),
      });

      // ⚠️ Read the text and parse under a guard: `response.json().catch(() => ({}))` discards the status, which is the
      // only fact left when a body is unreadable.
      const raw = await response.text();
      let body: { error?: string } & Partial<PlatformMessagingRecorded> = {};
      if (raw.length > 0) {
        try {
          body = JSON.parse(raw) as typeof body;
        } catch {
          body = {};
        }
      }

      if (!response.ok) {
        setError(body.error ?? `Le serveur a refusé l'enregistrement (${response.status}).`);
        return;
      }

      setRecorded(body as PlatformMessagingRecorded);
      setDirty(false);
      // The fiche re-reads: the month's figures and the allocation history both move with this write.
      router.refresh();
    } catch {
      setError("Impossible de joindre le serveur. Vérifiez que le tunnel est ouvert, puis réessayez.");
    } finally {
      setSubmitting(false);
    }
  }

  function edited<T>(setter: (value: T) => void) {
    return (value: T) => {
      setDirty(true);
      setter(value);
    };
  }

  return (
    <Sheet open={open} onOpenChange={openChanged}>
      <SheetTrigger asChild>
        <Button type="button">Enregistrer un forfait</Button>
      </SheetTrigger>

      <SheetContent
        side="bottom"
        className="inset-0 h-dvh max-h-dvh rounded-none p-0 lg:inset-auto lg:top-1/2 lg:left-1/2 lg:h-auto lg:max-h-[85dvh] lg:w-[calc(100%-2rem)] lg:max-w-lg lg:-translate-x-1/2 lg:-translate-y-1/2 lg:rounded-xl lg:border"
      >
        <form onSubmit={submit} className="flex h-full min-h-0 flex-col">
          <SheetHeader className="shrink-0 border-b border-border">
            <SheetTitle>Enregistrer un forfait de rappels</SheetTitle>
            <SheetDescription>
              {clinicName}
              {standingAllowance === null
                ? " · aucun forfait mensuel enregistré"
                : ` · ${standingAllowance} rappels par mois aujourd'hui`}
            </SheetDescription>
          </SheetHeader>

          <div className="min-h-0 flex-1 space-y-4 overflow-y-auto p-4">
            {recorded ? <Outcome recorded={recorded} /> : null}

            {error ? (
              <p className="rounded-md border border-destructive/40 p-3 text-sm text-destructive" role="alert">
                {error}
              </p>
            ) : null}

            <fieldset className="space-y-2" disabled={submitting}>
              <legend className="text-sm font-medium">Type d&apos;allocation</legend>

              {/* Radios rather than a select: two mutually exclusive options each needing a sentence of explanation,
                  and the explanation has to be readable without opening a picker. */}
              <label className="flex items-start gap-3 rounded-md border border-border p-3 coarse:min-h-11">
                <input
                  type="radio"
                  name={`${fieldId}-kind`}
                  className="mt-1 size-4"
                  checked={kind === "standing"}
                  onChange={() => edited(setKind)("standing")}
                />
                <span>
                  <span className="block text-sm font-medium">Forfait mensuel</span>
                  <span className="block text-xs text-muted-foreground">
                    Remplace le forfait actuel, à partir de maintenant. S&apos;il l&apos;augmente, il prend effet
                    immédiatement et libère les rappels en attente. S&apos;il le diminue, il prend effet le mois
                    prochain — un cabinet n&apos;est jamais coupé en pleine journée.
                  </span>
                </span>
              </label>

              <label className="flex items-start gap-3 rounded-md border border-border p-3 coarse:min-h-11">
                <input
                  type="radio"
                  name={`${fieldId}-kind`}
                  className="mt-1 size-4"
                  checked={kind === "topUp"}
                  onChange={() => edited(setKind)("topUp")}
                />
                <span>
                  <span className="block text-sm font-medium">Complément ponctuel</span>
                  <span className="block text-xs text-muted-foreground">
                    S&apos;ajoute au forfait d&apos;un seul mois — le mois en cours ({monthLabel}) ou un mois à venir.
                  </span>
                </span>
              </label>
            </fieldset>

            <div className="space-y-1.5">
              <Label htmlFor={`${fieldId}-messages`}>Nombre de rappels</Label>
              <Input
                id={`${fieldId}-messages`}
                type="number"
                inputMode="numeric"
                // Zero is a real monthly forfait (« ce cabinet n'envoie pas de rappels WhatsApp ») but not a real
                // complement, which is exactly what the server refuses — so the floor follows the mode.
                min={kind === "standing" ? 0 : 1}
                required
                value={messages}
                onChange={(e) => edited(setMessages)(e.target.value)}
                disabled={submitting}
              />
              {kind === "standing" ? (
                <p className="text-xs text-muted-foreground">
                  0 est une valeur possible : elle signifie que ce cabinet n&apos;envoie pas de rappels WhatsApp. Comme
                  toute diminution, elle prend effet le mois prochain.
                </p>
              ) : null}
            </div>

            {kind === "topUp" ? (
              <div className="space-y-1.5">
                <Label htmlFor={`${fieldId}-month`}>Mois concerné</Label>
                {/* `type="month"` gives the platform's own month picker and yields exactly the AAAA-MM the server
                    expects, so no parsing happens on either side. */}
                <Input
                  id={`${fieldId}-month`}
                  type="month"
                  required
                  value={month}
                  onChange={(e) => edited(setMonth)(e.target.value)}
                  disabled={submitting}
                />
                <p className="text-xs text-muted-foreground">
                  Le mois en cours ou un mois à venir. Un mois écoulé est refusé : il ne libérerait aucun rappel et
                  réécrirait un chiffre que le cabinet a déjà vu.
                </p>
              </div>
            ) : null}

            <div className="flex items-start gap-3">
              <input
                id={`${fieldId}-complimentary`}
                type="checkbox"
                className="mt-1 size-4"
                checked={complimentary}
                onChange={(e) => edited(setComplimentary)(e.target.checked)}
                disabled={submitting}
              />
              <div>
                <Label htmlFor={`${fieldId}-complimentary`}>Forfait offert</Label>
                <p className="text-xs text-muted-foreground">
                  Enregistré sans montant — ce n&apos;est pas la même chose qu&apos;un paiement de 0,000 DT, que le
                  serveur refuse.
                </p>
              </div>
            </div>

            {!complimentary ? (
              <>
                <div className="space-y-1.5">
                  <Label htmlFor={`${fieldId}-amount`}>Montant (DT)</Label>
                  <Input
                    id={`${fieldId}-amount`}
                    type="text"
                    inputMode="decimal"
                    placeholder="45,000"
                    value={amount}
                    onChange={(e) => edited(setAmount)(e.target.value)}
                    disabled={submitting}
                  />
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor={`${fieldId}-method`}>Moyen de paiement</Label>
                  <select
                    id={`${fieldId}-method`}
                    className="min-h-11 w-full rounded-md border border-input bg-transparent px-3 py-2 text-base"
                    value={method}
                    onChange={(e) => edited(setMethod)(e.target.value)}
                    disabled={submitting}
                  >
                    <option value="Transfer">Virement</option>
                    <option value="Cash">Espèces</option>
                    <option value="Cheque">Chèque</option>
                    <option value="Card">Carte bancaire</option>
                  </select>
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor={`${fieldId}-reference`}>Référence</Label>
                  <Input
                    id={`${fieldId}-reference`}
                    value={reference}
                    onChange={(e) => edited(setReference)(e.target.value)}
                    placeholder="N° de virement, de chèque ou de reçu"
                    disabled={submitting}
                  />
                </div>
              </>
            ) : null}

            <div className="space-y-1.5">
              <Label htmlFor={`${fieldId}-note`}>Note (facultatif)</Label>
              <Input
                id={`${fieldId}-note`}
                value={note}
                onChange={(e) => edited(setNote)(e.target.value)}
                disabled={submitting}
              />
            </div>
          </div>

          <div className="flex shrink-0 flex-col gap-2 border-t border-border p-4 sm:flex-row-reverse">
            <Button type="submit" disabled={submitting}>
              {submitting ? "Enregistrement…" : "Enregistrer le forfait"}
            </Button>
            <Button type="button" variant="outline" onClick={() => openChanged(false)} disabled={submitting}>
              {recorded ? "Fermer" : "Annuler"}
            </Button>
          </div>
        </form>
      </SheetContent>
    </Sheet>
  );
}

/**
 * AC-6.3/6.4's outcome, and AC-6.7's « déjà enregistré » said in words.
 *
 * ⚠️ **A lowering is the case this block exists for.** It comes back with next month's key and this month's figure
 * unchanged, which looks exactly like nothing happening — so it is stated. Without that, a vendor concludes the command
 * failed and tries again with a larger figure.
 *
 * ⚠️ A replay is a **success**, not a warning: the second tap found the allocation already recorded, which is the outcome
 * the vendor wanted. Telling them it failed would invite a third attempt.
 */
function Outcome({ recorded }: { recorded: PlatformMessagingRecorded }) {
  const deferred =
    recorded.kind === "Standing"
    && recorded.previousAllowanceThisMonth !== null
    && recorded.allowanceThisMonth === recorded.previousAllowanceThisMonth;

  return (
    <div className="rounded-md border border-border bg-muted/40 p-3 text-sm" role="status">
      <p className="font-medium">
        {recorded.alreadyRecorded ? "Ce forfait était déjà enregistré." : "Forfait enregistré."}
      </p>

      <p className="mt-1 text-muted-foreground">
        {recorded.kindLabel}
        {recorded.messages === null ? "" : ` · ${recorded.messages} rappels`}
        {recorded.effectiveMonthLabel ? ` · à partir de ${recorded.effectiveMonthLabel}` : ""}
      </p>

      <p className="mt-1 text-muted-foreground">
        Ce mois-ci : {recorded.allowanceThisMonth === null ? "non mesuré" : recorded.allowanceThisMonth} rappels
        {recorded.previousAllowanceThisMonth === null
          ? ""
          : ` (auparavant ${recorded.previousAllowanceThisMonth})`}
        {recorded.consumedThisMonth === null ? "" : ` · ${recorded.consumedThisMonth} déjà envoyés`}
      </p>

      {deferred ? (
        <p className="mt-2">
          Cette diminution prend effet en {recorded.effectiveMonthLabel} : le forfait du mois en cours est inchangé, à
          dessein. Le cabinet garde ce qui lui avait été annoncé.
        </p>
      ) : null}
    </div>
  );
}
