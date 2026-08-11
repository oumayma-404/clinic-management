"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";

import type { PlatformClinicPage, PlatformClinicRow } from "@/lib/api/platform";
import { CardList } from "@/components/ui/card-list";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { EM_DASH, formatCount, formatDate, formatDateTime, formatMoney } from "@/lib/format";

/**
 * The portfolio itself — one table above `lg:`, one card list below it (`platform-console` AC-2.1).
 *
 * ⚠️ **Two trees, not one that reflows.** Fourteen columns cannot be made readable at 320 px by any amount of
 * CSS on a `<table>`; see `card-list.tsx` on why `display: block` is the wrong answer even before the width is.
 * Both live in this one file so `check:responsive`'s `card-fallback` rule counts them together — a table that
 * grows a column here cannot quietly lose its small-screen form.
 *
 * ⚠️ **The breakpoint is `lg` (1024 px), not `md`.** A tablet in portrait is already past `md:` and would get
 * fourteen columns on a 768 px-wide screen; the plan says so explicitly, and it is the one place this app
 * departs from the clinic bundle's usual `md:` table boundary.
 *
 * ⚠️ **One row action, and it is an explicit link rather than a menu** (Part 3; the Part-2 note that said « no row
 * action, it arrives with its first action » is now satisfied). The plan's step 23 asks for « row actions in an
 * explicit menu on every width, nothing hover-only »; with exactly one action a dropdown would be a control whose
 * only purpose is to hide a single link behind a tap. What the requirement is actually about — no affordance
 * revealed by hover, and the same affordance at every width — is honoured: the link is always visible, in the table
 * and in the card list. The menu arrives with Part 4's three writes, which is when there is a choice to present.
 *
 * ⚠️ **The row is clickable, and the « Ouvrir » link is what makes that acceptable.** A `<tr>` with an `onClick` has
 * no keyboard path and no accessible role, so the click is an *addition* to the named link rather than a replacement
 * for it: a mouse gets the whole row, a keyboard and a screen reader get the same link they always had. (This note
 * used to say the row was deliberately not clickable — it was, and losing the link is what would have been wrong.)
 * A click landing on a text selection is ignored, because the new « Administrateur » column is there to be copied.
 */
export function ClinicPortfolio({ page }: { page: PlatformClinicPage }) {
  const router = useRouter();

  if (page.items.length === 0) {
    return (
      <p className="rounded-lg border border-border bg-card p-6 text-sm text-muted-foreground" role="status">
        Aucun cabinet ne correspond à ces critères.
      </p>
    );
  }

  return (
    <>
      <div className="hidden lg:block">
        <div className="rounded-lg border border-border bg-card">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead scope="col">Cabinet</TableHead>
                <TableHead scope="col">Administrateur</TableHead>
                <TableHead scope="col">État</TableHead>
                <TableHead scope="col" className="text-right">
                  Patients
                </TableHead>
                <TableHead scope="col" className="text-right">
                  Comptes
                </TableHead>
                <TableHead scope="col" className="text-right">
                  RDV pris (30 j)
                </TableHead>
                <TableHead scope="col" className="text-right">
                  Enreg. (7 j)
                </TableHead>
                <TableHead scope="col" className="text-right">
                  Enreg. (30 j)
                </TableHead>
                <TableHead scope="col" className="text-right">
                  Jours actifs
                </TableHead>
                <TableHead scope="col">Dernier enreg.</TableHead>
                <TableHead scope="col">Dernière connexion</TableHead>
                <TableHead scope="col" className="text-right">
                  Encaissé (cabinet)
                </TableHead>
                <TableHead scope="col">Créé le</TableHead>
                {/* The action column's header is not empty: a blank `<th>` leaves a screen reader announcing
                    « colonne 13 » for the one cell that does something. */}
                <TableHead scope="col">Fiche</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {page.items.map((clinic) => (
                <TableRow
                  key={clinic.clinicId}
                  className="cursor-pointer"
                  onClick={() => {
                    if (!window.getSelection()?.toString()) {
                      router.push(`/cabinets/${clinic.clinicId}`);
                    }
                  }}
                >
                  <TableCell>
                    <span className="font-medium">{clinic.name}</span>
                    <span className="block text-xs text-muted-foreground">{clinic.city ?? EM_DASH}</span>
                  </TableCell>
                  {/* An absent address is « aucun compte administrateur », not an unknown one — but that is a
                      sentence for the fiche; here the em dash is the column's own convention. */}
                  <TableCell className="max-w-[16rem] truncate" title={clinic.adminEmail ?? undefined}>
                    {clinic.adminEmail ?? EM_DASH}
                  </TableCell>
                  <TableCell>
                    <StateBadge clinic={clinic} />
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{measured(clinic, clinic.patients)}</TableCell>
                  <TableCell className="text-right tabular-nums">{measured(clinic, clinic.users)}</TableCell>
                  <TableCell className="text-right tabular-nums">{measured(clinic, clinic.appointments30d)}</TableCell>
                  <TableCell className="text-right tabular-nums">{measured(clinic, clinic.writes7d)}</TableCell>
                  <TableCell className="text-right tabular-nums">{measured(clinic, clinic.writes30d)}</TableCell>
                  <TableCell className="text-right tabular-nums">{measured(clinic, clinic.activeDays30d)}</TableCell>
                  <TableCell className="whitespace-nowrap">{formatDateTime(clinic.lastWriteAt)}</TableCell>
                  <TableCell className="whitespace-nowrap">{formatDateTime(clinic.lastLoginAt)}</TableCell>
                  <TableCell className="whitespace-nowrap text-right tabular-nums">
                    {clinic.countersComputedAt ? formatMoney(clinic.clinicCollectedThisMonthDt) : EM_DASH}
                  </TableCell>
                  <TableCell className="whitespace-nowrap">{formatDate(clinic.createdAt)}</TableCell>
                  <TableCell className="whitespace-nowrap">
                    <Link
                      href={`/cabinets/${clinic.clinicId}`}
                      className="underline underline-offset-4"
                      // Named for its row, so a screen reader reading the links of this table hears twelve distinct
                      // destinations rather than « Ouvrir » twelve times.
                      aria-label={`Ouvrir la fiche de ${clinic.name}`}
                    >
                      Ouvrir
                    </Link>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </div>

      <div className="lg:hidden">
        <CardList
          items={page.items}
          getKey={(clinic) => clinic.clinicId}
          title={(clinic) => clinic.name}
          subtitle={(clinic) => clinic.city ?? undefined}
          status={(clinic) => <StateBadge clinic={clinic} />}
          fields={(clinic) => [
            // First of the fields: on a phone the vendor is usually looking up who to write to. `CardList` drops a
            // field with no value, so a cabinet with no admin account shows no line rather than a dash.
            { label: "Administrateur", value: clinic.adminEmail ?? "" },
            // The two activity markers move into the field list below `lg:` — the status slot holds one badge and
            // the entitlement is what the vendor reads first. They are words, not colours (AC-6.3).
            clinic.countersComputedAt === null
              ? { label: "Compteurs", value: "Jamais mesuré" }
              : clinic.writes30d === 0
                ? { label: "Compteurs", value: "Dormant (30 j)" }
                : false,
            clinic.endsOn ? { label: "Fin d'abonnement", value: formatDate(clinic.endsOn) } : false,
            // Ordered by what a churn conversation needs first: is it being used, then how much money, then the
            // rest. The unmeasured cabinet drops every figure rather than showing zeros it cannot vouch for.
            clinic.countersComputedAt !== null && {
              label: "Enreg. (30 j)",
              value: formatCount(clinic.writes30d),
            },
            clinic.countersComputedAt !== null && {
              label: "Jours actifs (30 j)",
              value: formatCount(clinic.activeDays30d),
            },
            clinic.countersComputedAt !== null && {
              label: "Encaissé (cabinet)",
              value: formatMoney(clinic.clinicCollectedThisMonthDt),
            },
            clinic.countersComputedAt !== null && { label: "Patients", value: formatCount(clinic.patients) },
            clinic.countersComputedAt !== null && { label: "Comptes", value: formatCount(clinic.users) },
            clinic.countersComputedAt !== null && {
              label: "RDV pris (30 j)",
              value: formatCount(clinic.appointments30d),
            },
            { label: "Dernier enreg.", value: formatDateTime(clinic.lastWriteAt) },
            { label: "Dernière connexion", value: formatDateTime(clinic.lastLoginAt) },
            { label: "Créé le", value: formatDate(clinic.createdAt) },
            {
              // Last, and always present: the same affordance the table has, at every width, never behind a hover
              // or a long-press. The coarse-pointer 44 px floor in globals.css applies to it like every other link.
              label: "Fiche",
              value: (
                <Link
                  href={`/cabinets/${clinic.clinicId}`}
                  className="underline underline-offset-4"
                  aria-label={`Ouvrir la fiche de ${clinic.name}`}
                >
                  Ouvrir la fiche
                </Link>
              ),
            },
          ]}
        />
      </div>
    </>
  );
}

/**
 * A cabinet's entitlement state, in one place so the table and the card list cannot word it differently.
 *
 * ⚠️ **Text and shape, never colour alone** (AC-6.3). « Suspendu » and « Expiré » have different causes and
 * different remedies, and a reader who cannot distinguish two reds — or is reading a printout — must still be able
 * to tell them apart. The colour is an emphasis on top of a word that already says it.
 *
 * ⚠️ **A cabinet with no entitlement carries the server's own sentence** rather than an em dash: it is FR-13's
 * failure state, not a missing value, and « — » would read as « nous ne savons pas ».
 */
function StateBadge({ clinic }: { clinic: PlatformClinicRow }) {
  const tone =
    clinic.state === "Suspended"
      ? "border-destructive/60 text-destructive"
      : clinic.state === "Expired"
        ? "border-destructive/40 text-destructive"
        : clinic.state === null
          ? "border-destructive/40 text-destructive"
          : "border-border text-muted-foreground";

  const soon = clinic.daysRemaining !== null && clinic.daysRemaining <= 14;

  return (
    <span className="inline-flex flex-wrap items-center gap-1.5">
      <span className={`rounded-full border px-2 py-0.5 text-xs ${tone}`}>{clinic.stateLabel}</span>
      {soon ? (
        <span className="text-xs text-muted-foreground">
          {clinic.daysRemaining === 0 ? "dernier jour" : `${clinic.daysRemaining} j`}
        </span>
      ) : null}
    </span>
  );
}

/**
 * A figure the counter pass has actually measured, or the em dash.
 *
 * ⚠️ **Zero and « not measured yet » are different statements** (EC-15), and rendering the second as the first is
 * how a deployment whose nightly pass has never run reads as a portfolio of dead practices. The row's own
 * `countersComputedAt` is the only thing that can tell them apart, so every activity figure goes through here.
 */
function measured(clinic: PlatformClinicRow, value: number): string {
  return clinic.countersComputedAt === null ? EM_DASH : formatCount(value);
}
