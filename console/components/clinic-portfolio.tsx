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
 * ⚠️ **No row action and no clickable row, on purpose.** A cabinet's detail page is Part 3 and the three writes
 * are Part 4, so there is nothing to put in a menu today — and a menu that opens onto nothing, or a row that
 * looks clickable and is not, is a dead control. It arrives with its first action.
 */
export function ClinicPortfolio({ page }: { page: PlatformClinicPage }) {
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
              </TableRow>
            </TableHeader>
            <TableBody>
              {page.items.map((clinic) => (
                <TableRow key={clinic.clinicId}>
                  <TableCell>
                    <span className="font-medium">{clinic.name}</span>
                    <span className="block text-xs text-muted-foreground">{clinic.city ?? EM_DASH}</span>
                  </TableCell>
                  <TableCell className="text-muted-foreground">{stateLabel(clinic)}</TableCell>
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
          status={(clinic) =>
            clinic.countersComputedAt === null ? (
              <span className="rounded-full border border-border px-2 py-0.5 text-xs text-muted-foreground">
                Jamais mesuré
              </span>
            ) : clinic.writes30d === 0 ? (
              <span className="rounded-full border border-destructive/40 px-2 py-0.5 text-xs text-destructive">
                Dormant
              </span>
            ) : null
          }
          fields={(clinic) => [
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
          ]}
        />
      </div>
    </>
  );
}

/**
 * A cabinet's subscription state, or the em dash while the companion feature is unbuilt. One place, so the
 * table and the card list cannot word the gap differently — and one place for Part 4 to replace.
 */
function stateLabel(clinic: PlatformClinicRow): string {
  return clinic.state ?? EM_DASH;
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
