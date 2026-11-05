import Link from "next/link";

import { CardList } from "@/components/ui/card-list";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { PlatformFeedback, PlatformFeedbackPage } from "@/lib/api/platform";
import { formatDateTime } from "@/lib/format";

/**
 * « Retours » itself — one table above `lg:`, one card list below it (`guided-first-days` E, S14).
 *
 * ⚠️ **Two trees in this one file**, so `check:responsive`'s `card-fallback` rule counts them together.
 *
 * ⚠️ **Read-only.** A message a cabinet sent is not something the vendor edits; the only affordances are the link
 * to the cabinet's fiche and, when the person agreed, their address.
 */
export function FeedbackList({ page }: { page: PlatformFeedbackPage }) {
  if (page.items.length === 0) {
    return (
      <p className="rounded-lg border border-border bg-card p-6 text-sm text-muted-foreground" role="status">
        Aucun retour pour ces critères.
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
                <TableHead scope="col">Date</TableHead>
                <TableHead scope="col">Cabinet</TableHead>
                <TableHead scope="col">Qui</TableHead>
                <TableHead scope="col">Type</TableHead>
                <TableHead scope="col">Notes</TableHead>
                <TableHead scope="col">Message</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {page.items.map((item) => (
                <TableRow key={item.feedbackId}>
                  <TableCell className="whitespace-nowrap">{formatDateTime(item.createdAt)}</TableCell>
                  <TableCell>
                    <Link href={`/cabinets/${item.clinicId}`} className="underline underline-offset-4">
                      {item.clinicName}
                    </Link>
                  </TableCell>
                  <TableCell>
                    <span>{item.authorName}</span>
                    <span className="mt-0.5 block text-xs text-muted-foreground">{item.authorRoleLabel}</span>
                    {item.contactEmail ? (
                      <a href={`mailto:${item.contactEmail}`} className="mt-0.5 block break-all text-xs underline">
                        {item.contactEmail}
                      </a>
                    ) : null}
                  </TableCell>
                  <TableCell className="whitespace-nowrap">{item.kindLabel}</TableCell>
                  <TableCell className="text-sm text-muted-foreground">{notes(item) ?? "—"}</TableCell>
                  <TableCell>
                    {item.message ? <span className="whitespace-pre-line">{item.message}</span> : "—"}
                    {item.fromScreen ? (
                      <span className="mt-0.5 block text-xs text-muted-foreground">Écran : {item.fromScreen}</span>
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
          getKey={(item) => item.feedbackId}
          title={(item) => item.clinicName}
          subtitle={(item) => `${item.kindLabel} · ${formatDateTime(item.createdAt)}`}
          fields={(item) => [
            { label: "Qui", value: `${item.authorName} · ${item.authorRoleLabel}` },
            ...(notes(item) ? [{ label: "Notes", value: notes(item) }] : []),
            ...(item.message
              ? [{ label: "Message", value: <span className="whitespace-pre-line">{item.message}</span> }]
              : []),
            ...(item.fromScreen ? [{ label: "Écran", value: item.fromScreen }] : []),
            ...(item.contactEmail
              ? [
                  {
                    label: "Recontacter",
                    value: (
                      <a href={`mailto:${item.contactEmail}`} className="break-all underline">
                        {item.contactEmail}
                      </a>
                    ),
                  },
                ]
              : []),
            {
              label: "Cabinet",
              value: (
                <Link href={`/cabinets/${item.clinicId}`} className="underline underline-offset-4">
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

/** « Aime : Agenda, la prise de RDV » — the areas picked, then the person's own « Autre », or nothing. */
function liste(label: string, areas: string[], other: string | null): string | null {
  const parts = other ? [...areas, other] : areas;
  return parts.length > 0 ? `${label} : ${parts.join(", ")}` : null;
}

/** « aide 4/5 · facile 3/5 · Aime : Agenda · Difficile : Devis » — the week-one answers in one line, or nothing. */
function notes(item: PlatformFeedback): string | null {
  const parts = [
    item.helpRating !== null ? `aide ${item.helpRating}/5` : null,
    item.easeRating !== null ? `facile ${item.easeRating}/5` : null,
    liste("Aime", item.likedAreas, item.likedOther),
    liste("Difficile", item.hardAreas, item.hardOther),
  ].filter((part): part is string => part !== null);
  return parts.length > 0 ? parts.join(" · ") : null;
}
