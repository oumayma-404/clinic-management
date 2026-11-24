"use client"

import type { ReactNode } from "react"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import type { PatientLegacyHistoryEntryDto } from "@/lib/api/patient-legacy-history"
import { formatCalendarDay } from "@/lib/format"

type Entry = PatientLegacyHistoryEntryDto

/** One médicament of an old ordonnance — the import wrote each as « NOM (voie) — posologie · note ». */
interface PrescriptionRow {
  id: string
  date: string | null
  medicament: string
  posologie: string
  note: string
}

const KNOWN = new Set(["Diagnostic", "Consultation", "Ordonnance", "Radio"])
const day = (d: string | null) => formatCalendarDay(d, "Sans date")

/** Split on the LAST « · » (the note) then the FIRST « — » (the posologie): both halves are optional. */
function prescriptionRows(entry: Entry): PrescriptionRow[] {
  return (entry.details ?? "")
    .split("\n")
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line, i) => {
      const n = line.lastIndexOf(" · ")
      const main = n >= 0 ? line.slice(0, n) : line
      const d = main.indexOf(" — ")
      return {
        id: `${entry.id}-${i}`,
        date: entry.date,
        medicament: d >= 0 ? main.slice(0, d) : main,
        posologie: d >= 0 ? main.slice(d + 3) : "",
        note: n >= 0 ? line.slice(n + 3) : "",
      }
    })
}

/**
 * « Ancien logiciel » laid out like the old software's own PDF: Diagnostic, then one table per history —
 * Consultations (Actes · Compte rendu · Date), Ordonnances (Médicament · Posologie · Note · Date), Radios
 * (Radio · Note · Date). Read-only. Tables from `lg:`, cards below, like the patient file's other panels.
 */
export function LegacyHistoryView({ entries }: { entries: Entry[] }) {
  const of = (c: string) => entries.filter((e) => e.category === c)
  const diagnostics = of("Diagnostic")
  const consultations = of("Consultation")
  const prescriptions = of("Ordonnance").flatMap(prescriptionRows)
  const radios = of("Radio")
  const others = entries.filter((e) => !KNOWN.has(e.category))

  return (
    <div className="space-y-6">
      {diagnostics.length > 0 && (
        <Group title="Diagnostic">
          <div className="space-y-2 rounded-md border bg-card p-3">
            {diagnostics.map((e) => (
              <p key={e.id} className="whitespace-pre-line break-words text-sm">
                {e.details}
              </p>
            ))}
          </div>
        </Group>
      )}

      {consultations.length > 0 && (
        <Group title="Consultations" count={consultations.length}>
          <div className="rounded-md border bg-card">
            <CardList
              className={CARDS_ONLY_LG}
              ariaLabel="Consultations de l'ancien logiciel"
              items={consultations}
              getKey={(e) => e.id}
              title={(e) => e.title}
              fields={(e) => [
                { label: "Date", value: day(e.date) },
                { label: "Compte rendu", value: e.details ? <span className="whitespace-pre-line">{e.details}</span> : null },
              ]}
            />
            <Table containerClassName={TABLE_ONLY_LG}>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-1/4">Actes</TableHead>
                  <TableHead>Compte rendu</TableHead>
                  <TableHead className="w-28">Date</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {consultations.map((e) => (
                  <TableRow key={e.id} className="align-top">
                    <TableCell className="font-medium">{e.title}</TableCell>
                    <TableCell className="whitespace-pre-line text-muted-foreground">{e.details}</TableCell>
                    <TableCell className="whitespace-nowrap tabular-nums">{day(e.date)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </Group>
      )}

      {prescriptions.length > 0 && (
        <Group title="Ordonnances" count={of("Ordonnance").length}>
          <div className="rounded-md border bg-card">
            <CardList
              className={CARDS_ONLY_LG}
              ariaLabel="Ordonnances de l'ancien logiciel"
              items={prescriptions}
              getKey={(r) => r.id}
              title={(r) => r.medicament}
              fields={(r) => [
                { label: "Date", value: day(r.date) },
                { label: "Posologie", value: r.posologie || null },
                { label: "Note", value: r.note || null },
              ]}
            />
            <Table containerClassName={TABLE_ONLY_LG}>
              <TableHeader>
                <TableRow>
                  <TableHead>Médicament</TableHead>
                  <TableHead>Posologie</TableHead>
                  <TableHead>Note</TableHead>
                  <TableHead className="w-28">Date</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {prescriptions.map((r) => (
                  <TableRow key={r.id} className="align-top">
                    <TableCell className="font-medium">{r.medicament}</TableCell>
                    <TableCell>{r.posologie}</TableCell>
                    <TableCell className="text-muted-foreground">{r.note}</TableCell>
                    <TableCell className="whitespace-nowrap tabular-nums">{day(r.date)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </Group>
      )}

      {radios.length > 0 && (
        <Group title="Radios" count={radios.length}>
          <div className="rounded-md border bg-card">
            <CardList
              className={CARDS_ONLY_LG}
              ariaLabel="Radios de l'ancien logiciel"
              items={radios}
              getKey={(e) => e.id}
              title={(e) => e.title}
              fields={(e) => [
                { label: "Date", value: day(e.date) },
                { label: "Note", value: e.details },
              ]}
            />
            <Table containerClassName={TABLE_ONLY_LG}>
              <TableHeader>
                <TableRow>
                  <TableHead>Radio</TableHead>
                  <TableHead>Note</TableHead>
                  <TableHead className="w-28">Date</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {radios.map((e) => (
                  <TableRow key={e.id} className="align-top">
                    <TableCell className="font-medium">{e.title}</TableCell>
                    <TableCell className="text-muted-foreground">{e.details}</TableCell>
                    <TableCell className="whitespace-nowrap tabular-nums">{day(e.date)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </Group>
      )}

      {others.length > 0 && (
        <Group title="Autres">
          <ul className="space-y-2 rounded-md border bg-card p-3 text-sm">
            {others.map((e) => (
              <li key={e.id} className="break-words">
                <span className="font-medium">{e.title}</span>
                <span className="text-muted-foreground"> · {e.category} · {day(e.date)}</span>
                {e.details && <p className="whitespace-pre-line text-muted-foreground">{e.details}</p>}
              </li>
            ))}
          </ul>
        </Group>
      )}
    </div>
  )
}

function Group({ title, count, children }: { title: string; count?: number; children: ReactNode }) {
  return (
    <section className="space-y-2">
      <h3 className="text-sm font-semibold">
        {title}
        {count != null && <span className="ms-1.5 font-normal text-muted-foreground">({count})</span>}
      </h3>
      {children}
    </section>
  )
}
