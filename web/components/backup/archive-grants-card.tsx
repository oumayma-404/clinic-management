"use client"

import { useCallback, useEffect, useState } from "react"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog"
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent,
  AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { Laptop, Plus, Copy, Check } from "lucide-react"
import { toast } from "sonner"
import { backupApi, type ArchiveGrantDto, type IssuedArchiveGrantDto } from "@/lib/api/backup"
import { showErrorToast } from "@/lib/errors"
import { formatDateTime, quoteFr } from "@/lib/format"

/**
 * « Postes autorisés » — which machines may pull this cabinet's archive unattended
 * (`clinic-archive-auto-copy`).
 *
 * ⚠️ **The secret is shown once and is unrecoverable.** The server stores a SHA-256 and no read returns the
 * plaintext, so the issue dialog below is the only moment it exists outside the machine it is pasted into. That
 * is why it gets its own dialog with a copy control rather than a toast, and why the dialog says so.
 *
 * ⚠️ **Revoked grants stay in the list.** « ce poste ne peut plus » is as much of the answer as « ce poste
 * peut », and hiding them would make a revocation look like a deletion of history.
 */
export function ArchiveGrantsCard() {
  const [grants, setGrants] = useState<ArchiveGrantDto[]>([])
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)

  const [issueOpen, setIssueOpen] = useState(false)
  const [label, setLabel] = useState("")
  const [issuing, setIssuing] = useState(false)
  const [issueError, setIssueError] = useState<string | null>(null)
  const [issued, setIssued] = useState<IssuedArchiveGrantDto | null>(null)
  const [copied, setCopied] = useState(false)

  const [toRevoke, setToRevoke] = useState<ArchiveGrantDto | null>(null)
  const [revoking, setRevoking] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    setFailed(false)
    try {
      setGrants(await backupApi.archiveGrants())
    } catch {
      // A failed read is not « aucun poste autorisé » — on this card the wrong one is actively reassuring,
      // since it would say nothing can pull the cabinet's record when something may.
      setFailed(true)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const handleIssue = async () => {
    const trimmed = label.trim()
    if (!trimmed) {
      setIssueError("Donnez un nom au poste, pour pouvoir le révoquer.")
      return
    }

    setIssuing(true)
    setIssueError(null)
    try {
      const grant = await backupApi.issueArchiveGrant(trimmed)
      setIssued(grant)
      setIssueOpen(false)
      setLabel("")
      void load()
    } catch (err) {
      // The dialog stays open with the name as typed — § 13.
      setIssueError(err instanceof Error ? err.message : "Le poste n'a pas pu être autorisé.")
    } finally {
      setIssuing(false)
    }
  }

  const handleRevoke = async () => {
    if (!toRevoke) return
    setRevoking(true)
    try {
      await backupApi.revokeArchiveGrant(toRevoke.id)
      toast.success(`Poste ${quoteFr(toRevoke.label)} révoqué`)
      setToRevoke(null)
      void load()
    } catch (err) {
      showErrorToast(err, "Le poste n'a pas pu être révoqué.")
    } finally {
      setRevoking(false)
    }
  }

  const copySecret = async () => {
    if (!issued) return
    try {
      await navigator.clipboard.writeText(issued.secret)
      setCopied(true)
      toast.success("Clé copiée")
    } catch {
      // Clipboard access is refused in some contexts; the value is on screen and selectable, so this is a
      // convenience failing rather than the capability being lost.
      showErrorToast(null, "La copie a échoué — sélectionnez la clé et copiez-la à la main.")
    }
  }

  const statusBadge = (grant: ArchiveGrantDto) =>
    grant.revokedAtUtc ? (
      <Badge variant="secondary">Révoqué</Badge>
    ) : (
      <Badge className="bg-success-wash text-success">Actif</Badge>
    )

  const lastUsed = (grant: ArchiveGrantDto) =>
    grant.lastUsedAtUtc ? formatDateTime(grant.lastUsedAtUtc) : "Jamais utilisé"

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="min-w-0">
          <h3 className="text-base font-medium">Postes autorisés</h3>
          <p className="text-sm text-muted-foreground">
            Les machines qui peuvent télécharger l&apos;archive du cabinet automatiquement, sans confirmation.
          </p>
        </div>
        <Button size="sm" className="min-h-11" onClick={() => { setIssueError(null); setIssueOpen(true) }}>
          <Plus className="mr-1.5 h-4 w-4" />
          Autoriser un poste
        </Button>
      </div>

      {failed ? (
        <LoadFailureNotice message="Les postes autorisés n'ont pas pu être lus." onRetry={load} />
      ) : loading ? (
        <div className="space-y-2" aria-hidden="true">
          <div className="h-14 animate-pulse rounded-md bg-muted/60" />
          <div className="h-14 animate-pulse rounded-md bg-muted/60" />
        </div>
      ) : grants.length === 0 ? (
        <EmptyState
          size="compact"
          icon={Laptop}
          title="Aucun poste autorisé"
          description="Autorisez le poste sur lequel APEXA doit déposer une copie de l'archive."
        />
      ) : (
        <div className="rounded-md border bg-card">
          <CardList
            className={CARDS_ONLY}
            ariaLabel="Postes autorisés"
            items={grants}
            getKey={(g) => g.id}
            title={(g) => g.label}
            status={(g) => statusBadge(g)}
            fields={(g) => [
              { label: "Dernière copie", value: lastUsed(g) },
              { label: "Autorisé le", value: formatDateTime(g.createdAtUtc) },
            ]}
            actions={(g) =>
              g.revokedAtUtc ? null : (
                <Button
                  variant="outline"
                  size="sm"
                  className="min-h-11"
                  onClick={() => setToRevoke(g)}
                >
                  Révoquer
                </Button>
              )
            }
          />

          <Table containerClassName={TABLE_ONLY}>
            <TableHeader>
              <TableRow>
                <TableHead>Poste</TableHead>
                <TableHead>État</TableHead>
                <TableHead>Dernière copie</TableHead>
                <TableHead className="whitespace-nowrap">Autorisé le</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {grants.map((g) => (
                <TableRow key={g.id}>
                  <TableCell className="font-medium">{g.label}</TableCell>
                  <TableCell>{statusBadge(g)}</TableCell>
                  <TableCell className="text-muted-foreground">{lastUsed(g)}</TableCell>
                  <TableCell className="whitespace-nowrap text-muted-foreground">
                    {formatDateTime(g.createdAtUtc)}
                  </TableCell>
                  <TableCell className="text-right">
                    {g.revokedAtUtc ? null : (
                      <Button
                        variant="outline"
                        size="sm"
                        className="coarse:min-h-11"
                        onClick={() => setToRevoke(g)}
                      >
                        Révoquer
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {/* Issue — a name, and nothing else to decide. */}
      <Dialog open={issueOpen} onOpenChange={(open) => { if (!issuing) setIssueOpen(open) }}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Autoriser un poste</DialogTitle>
            <DialogDescription>
              Nommez la machine, pour pouvoir la reconnaître et la révoquer plus tard.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4">
            <FormErrorBanner message={issueError} />
            <div className="space-y-2">
              <Label htmlFor="grant-label">Nom du poste</Label>
              <Input
                id="grant-label"
                value={label}
                autoFocus
                disabled={issuing}
                placeholder="Portable du Dr Ben Salah"
                onChange={(e) => setLabel(e.target.value)}
                onKeyDown={(e) => { if (e.key === "Enter") void handleIssue() }}
              />
            </div>
          </div>

          <DialogFooter>
            <Button variant="outline" className="min-h-11" disabled={issuing} onClick={() => setIssueOpen(false)}>
              Annuler
            </Button>
            <Button className="min-h-11" disabled={issuing} onClick={handleIssue}>
              {issuing ? "Autorisation…" : "Autoriser"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* The one-time secret. Its own dialog because it cannot be recovered — a toast would be gone in seconds. */}
      <Dialog open={issued !== null} onOpenChange={(open) => { if (!open) { setIssued(null); setCopied(false) } }}>
        <DialogContent className="md:max-w-lg">
          <DialogHeader>
            <DialogTitle>Clé du poste {issued ? quoteFr(issued.label) : ""}</DialogTitle>
            <DialogDescription>
              Copiez-la maintenant et collez-la dans l&apos;application du poste. Elle ne sera plus jamais
              affichée : si elle est perdue, révoquez ce poste et autorisez-en un nouveau.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-3">
            {/* `break-all` and not `truncate`: hiding the tail of a value that cannot be recovered is the defect,
                not the fix. It is selectable, so the copy button failing costs nothing. */}
            <p className="rounded-md border bg-muted/40 p-3 font-mono text-sm break-all">
              {issued?.secret}
            </p>
            <Button variant="outline" className="min-h-11 w-full sm:w-auto" onClick={copySecret}>
              {copied ? <Check className="mr-1.5 h-4 w-4" /> : <Copy className="mr-1.5 h-4 w-4" />}
              {copied ? "Copiée" : "Copier la clé"}
            </Button>
          </div>

          <DialogFooter>
            <Button className="min-h-11" onClick={() => { setIssued(null); setCopied(false) }}>
              J&apos;ai copié la clé
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <AlertDialog open={toRevoke !== null} onOpenChange={(open) => { if (!open && !revoking) setToRevoke(null) }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              Révoquer {toRevoke ? quoteFr(toRevoke.label) : "ce poste"} ?
            </AlertDialogTitle>
            <AlertDialogDescription>
              Ce poste ne pourra plus télécharger l&apos;archive du cabinet. Les copies qu&apos;il a déjà
              déposées restent sur cette machine. La révocation est définitive : pour l&apos;autoriser à
              nouveau, il faudra créer une nouvelle clé.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={revoking}>Annuler</AlertDialogCancel>
            <AlertDialogAction variant="destructive" disabled={revoking} onClick={handleRevoke}>
              {revoking ? "Révocation…" : "Révoquer"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}
