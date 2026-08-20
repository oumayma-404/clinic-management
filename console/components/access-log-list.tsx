import Link from "next/link";

import { CardList } from "@/components/ui/card-list";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { PlatformAccessLogPage } from "@/lib/api/platform";
import { formatDateTime } from "@/lib/format";

/**
 * « Journal » itself — one table above `lg:`, one card list below it (`platform-console` FR-5, AC-7.3).
 *
 * ⚠️ **Two trees, not one that reflows**, and both in this file so `check:responsive`'s `card-fallback` rule counts
 * them together: a column added here cannot quietly lose its small-screen form.
 *
 * ⚠️ **Nothing here edits or deletes a row**, and there is no control that looks as though it might. The endpoint
 * has no write action at all — a ledger somebody can correct is not evidence — so the only affordances are the two
 * links that narrow it.
 */
export function AccessLogList({ page }: { page: PlatformAccessLogPage }) {
  if (page.items.length === 0) {
    return (
      <p className="rounded-lg border border-border bg-card p-6 text-sm text-muted-foreground" role="status">
        Aucun accès enregistré pour ces critères. Ouvrir la fiche d&apos;un cabinet en crée un ; afficher la liste
        des cabinets n&apos;en crée pas.
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
                <TableHead scope="col">Quand</TableHead>
                <TableHead scope="col">Compte console</TableHead>
                <TableHead scope="col">Cabinet</TableHead>
                <TableHead scope="col">Action</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {page.items.map((entry) => (
                <TableRow key={entry.entryId}>
                  <TableCell className="whitespace-nowrap">{formatDateTime(entry.occurredAt)}</TableCell>
                  <TableCell>{entry.accountEmail}</TableCell>
                  <TableCell>
                    {/* The cabinet's name is the row's own copy, so this still reads for a closed cabinet — the
                        link may 404 into « ce cabinet n'existe plus », which is the honest destination. */}
                    <Link
                      href={`/cabinets/${entry.clinicId}`}
                      className="underline underline-offset-4"
                    >
                      {entry.clinicName}
                    </Link>
                  </TableCell>
                  {/*
                    ⚠️ The target and the motif render **under** the action rather than as two more columns. Only
                    one action in nine populates them, so columns would be empty on every other row — and this is
                    the one row type whose whole evidentiary value is those two facts, which a narrow truncating
                    cell would hide. Everything else in this ledger acts on a cabinet and needs no such line.
                  */}
                  <TableCell className="text-muted-foreground">
                    <span>{entry.actionLabel}</span>
                    {entry.targetEmail ? (
                      <span className="mt-0.5 block text-xs">Compte : {entry.targetEmail}</span>
                    ) : null}
                    {entry.reason ? (
                      <span className="mt-0.5 block text-xs italic">« {entry.reason} »</span>
                    ) : null}
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
          getKey={(entry) => entry.entryId}
          title={(entry) => entry.clinicName}
          subtitle={(entry) => entry.actionLabel}
          fields={(entry) => [
            { label: "Quand", value: formatDateTime(entry.occurredAt) },
            { label: "Compte console", value: entry.accountEmail },
            {
              label: "Cabinet",
              value: (
                <Link href={`/cabinets/${entry.clinicId}`} className="underline underline-offset-4">
                  Ouvrir la fiche
                </Link>
              ),
            },
            // ⚠️ Spread rather than rendered conditionally in place: `fields` is a flat list, and an entry whose
            // `value` is null still draws its label — « Compte : — » on eight row types out of nine, which reads
            // as a missing value rather than an inapplicable one.
            ...(entry.targetEmail ? [{ label: "Compte", value: entry.targetEmail }] : []),
            ...(entry.reason ? [{ label: "Motif", value: entry.reason }] : []),
          ]}
        />
      </div>
    </>
  );
}
