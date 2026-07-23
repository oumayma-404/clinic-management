"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { toast } from "sonner"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Label } from "@/components/ui/label"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Users, KeyRound, UserX, UserCheck, RefreshCw, Copy, Check } from "lucide-react"
import { usersApi, type ClinicUserDto } from "@/lib/api/users"
import { clinicsApi } from "@/lib/api/clinics"
import { ApiError } from "@/lib/api/client"
import { useSession } from "@/lib/auth/session"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"

type PendingAction =
  | { type: "reset"; user: ClinicUserDto }
  | { type: "status"; user: ClinicUserDto }
  | { type: "regenerate" }

export function UserManagement() {
  const { user: currentUser } = useSession()
  const [users, setUsers] = useState<ClinicUserDto[]>([])
  const [clinicCode, setClinicCode] = useState<string>("")
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [pending, setPending] = useState<PendingAction | null>(null)
  const [working, setWorking] = useState(false)

  // Temp password shown once after a reset (AC-5.2).
  const [tempPassword, setTempPassword] = useState<{ email?: string; password: string } | null>(null)
  const [copied, setCopied] = useState(false)

  // Guards against setState after unmount (the admin can navigate away mid-load or mid-refresh).
  const mountedRef = useRef(true)
  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
    }
  }, [])

  const loadData = useCallback(async () => {
    try {
      setLoading(true)
      setError(null)
      const [userList, status] = await Promise.all([
        usersApi.list(),
        clinicsApi.getUserStatus(),
      ])
      if (!mountedRef.current) return
      setUsers(userList)
      setClinicCode(status.clinic?.code || "")
    } catch (err) {
      if (!mountedRef.current) return
      const message = err instanceof ApiError ? err.message : "Échec du chargement des utilisateurs."
      setError(message)
    } finally {
      if (mountedRef.current) setLoading(false)
    }
  }, [])

  useEffect(() => {
    loadData()
  }, [loadData])

  // Real-time: refetch the users table + clinic code when any client of this clinic changes a user
  // (reset password / activate / deactivate) or registers.
  useClinicRealtime(RealtimeResource.Users, loadData)

  const confirmAction = async () => {
    if (!pending) return
    setWorking(true)
    try {
      if (pending.type === "reset") {
        const result = await usersApi.resetPassword(pending.user.id)
        setTempPassword({ email: pending.user.email, password: result.temporaryPassword })
        toast.success("Mot de passe réinitialisé. Communiquez le mot de passe temporaire à l'utilisateur.")
        await loadData()
      } else if (pending.type === "status") {
        const nextActive = !pending.user.isActive
        await usersApi.setStatus(pending.user.id, nextActive)
        toast.success(nextActive ? "Utilisateur réactivé." : "Utilisateur désactivé.")
        await loadData()
      } else if (pending.type === "regenerate") {
        const clinic = await clinicsApi.regenerateCode()
        setClinicCode(clinic.code || "")
        toast.success("Code de la clinique régénéré. L'ancien code ne fonctionne plus.")
      }
      setPending(null)
    } catch (err) {
      const message = err instanceof ApiError ? err.message : "L'action a échoué. Veuillez réessayer."
      toast.error(message)
    } finally {
      setWorking(false)
    }
  }

  const copyTempPassword = async () => {
    if (!tempPassword) return
    try {
      await navigator.clipboard.writeText(tempPassword.password)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard unavailable — the password is still shown for manual copy.
    }
  }

  const roleLabel = (role: string) => {
    const map: Record<string, string> = { admin: "Administrateur", doctor: "Médecin", secretary: "Secrétaire" }
    return map[role?.toLowerCase()] ?? (role.charAt(0).toUpperCase() + role.slice(1))
  }
  const formatDate = (value?: string) =>
    value ? new Date(value).toLocaleString("fr-FR") : "Jamais"

  return (
    <div className="min-h-screen bg-gray-50 dark:bg-slate-950">
      <div className="mx-auto max-w-5xl space-y-4 p-4">
        <div className="mb-2 flex items-center gap-2">
          <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-blue-600">
            <Users className="h-4 w-4 text-white" />
          </div>
          <div>
            <h1 className="text-xl font-bold text-gray-900 dark:text-white">Gestion des utilisateurs</h1>
            <p className="text-xs text-muted-foreground">Gérez les comptes de la clinique et le code d'auto-inscription</p>
          </div>
        </div>

        {/* Clinic code + regenerate (AC-4.5) */}
        <Card className="border border-gray-200 dark:border-slate-800">
          <CardHeader className="pb-3">
            <CardTitle className="text-base">Code de la clinique</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <Label className="text-xs text-muted-foreground">À communiquer au personnel pour créer un compte</Label>
                <div className="mt-1.5">
                  {clinicCode ? (
                    <Badge
                      variant="outline"
                      className="border-blue-300 bg-white px-3 py-1 font-mono text-base font-bold text-blue-700 dark:border-blue-700 dark:bg-slate-900 dark:text-blue-300"
                    >
                      {clinicCode}
                    </Badge>
                  ) : (
                    <span className="text-sm text-muted-foreground">Aucun code défini</span>
                  )}
                </div>
              </div>
              <Button
                variant="outline"
                size="sm"
                className="gap-2"
                onClick={() => setPending({ type: "regenerate" })}
                disabled={working}
              >
                <RefreshCw className="h-4 w-4" />
                Régénérer
              </Button>
            </div>
          </CardContent>
        </Card>

        {/* Users list (AC-5.1) */}
        <Card className="border border-gray-200 dark:border-slate-800">
          <CardHeader className="pb-3">
            <CardTitle className="flex items-center gap-2 text-base">
              Utilisateurs
              <Badge variant="secondary">{users.length}</Badge>
            </CardTitle>
          </CardHeader>
          <CardContent>
            {error && (
              <div className="mb-4 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200">
                {error}
              </div>
            )}
            {loading ? (
              <p className="py-8 text-center text-muted-foreground">Chargement des utilisateurs…</p>
            ) : (
              <div className="overflow-x-auto">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Nom</TableHead>
                      <TableHead>Email</TableHead>
                      <TableHead>Rôle</TableHead>
                      <TableHead>Statut</TableHead>
                      <TableHead>Dernière connexion</TableHead>
                      <TableHead className="text-right">Actions</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {users.length === 0 ? (
                      <TableRow>
                        <TableCell colSpan={6} className="h-24 text-center text-muted-foreground">
                          Aucun utilisateur
                        </TableCell>
                      </TableRow>
                    ) : (
                      users.map((user) => {
                        // Backend forbids self-deactivation (would be an unrecoverable lockout);
                        // mirror that here so the action isn't offered as a dead end.
                        const isSelf = !!user.email && user.email === currentUser?.email
                        return (
                        <TableRow key={user.id}>
                          <TableCell className="font-medium text-foreground">{user.fullName || "-"}</TableCell>
                          <TableCell className="text-muted-foreground">{user.email || "-"}</TableCell>
                          <TableCell>
                            <Badge variant="outline">{roleLabel(user.role)}</Badge>
                          </TableCell>
                          <TableCell>
                            <div className="flex flex-wrap items-center gap-1.5">
                              {user.isActive ? (
                                <Badge className="bg-green-600 hover:bg-green-600">Actif</Badge>
                              ) : (
                                <Badge variant="destructive">Inactif</Badge>
                              )}
                              {user.mustChangePassword && (
                                <Badge variant="secondary" className="text-[10px]">Doit changer le mot de passe</Badge>
                              )}
                            </div>
                          </TableCell>
                          <TableCell className="text-muted-foreground">{formatDate(user.lastLoginAt)}</TableCell>
                          <TableCell className="text-right">
                            <div className="flex justify-end gap-2">
                              <Button
                                variant="ghost"
                                size="sm"
                                className="h-8 gap-1"
                                onClick={() => setPending({ type: "reset", user })}
                              >
                                <KeyRound className="h-3 w-3" />
                                Réinitialiser le mot de passe
                              </Button>
                              {user.isActive ? (
                                <Button
                                  variant="ghost"
                                  size="sm"
                                  className="h-8 gap-1 text-destructive hover:text-destructive"
                                  onClick={() => setPending({ type: "status", user })}
                                  disabled={isSelf}
                                  title={isSelf ? "Vous ne pouvez pas désactiver votre propre compte" : undefined}
                                >
                                  <UserX className="h-3 w-3" />
                                  Désactiver
                                </Button>
                              ) : (
                                <Button
                                  variant="ghost"
                                  size="sm"
                                  className="h-8 gap-1"
                                  onClick={() => setPending({ type: "status", user })}
                                >
                                  <UserCheck className="h-3 w-3" />
                                  Réactiver
                                </Button>
                              )}
                            </div>
                          </TableCell>
                        </TableRow>
                        )
                      })
                    )}
                  </TableBody>
                </Table>
              </div>
            )}
          </CardContent>
        </Card>
      </div>

      {/* Confirm dialog for reset / status / regenerate */}
      <AlertDialog open={pending !== null} onOpenChange={(open) => !open && setPending(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {pending?.type === "reset" && "Réinitialiser le mot de passe de cet utilisateur ?"}
              {pending?.type === "status" &&
                (pending.user.isActive ? "Désactiver cet utilisateur ?" : "Réactiver cet utilisateur ?")}
              {pending?.type === "regenerate" && "Régénérer le code de la clinique ?"}
            </AlertDialogTitle>
            <AlertDialogDescription>
              {pending?.type === "reset" && (
                <>
                  Un mot de passe temporaire sera généré pour{" "}
                  <span className="font-semibold">{pending.user.email}</span>. L'utilisateur devra le
                  changer à la prochaine connexion.
                </>
              )}
              {pending?.type === "status" && pending.user.isActive && (
                <>
                  <span className="font-semibold">{pending.user.email}</span> ne pourra plus se connecter.
                  Ses données historiques sont conservées.
                </>
              )}
              {pending?.type === "status" && !pending.user.isActive && (
                <>
                  <span className="font-semibold">{pending.user.email}</span> pourra de nouveau se connecter.
                </>
              )}
              {pending?.type === "regenerate" &&
                "Le code actuel cessera de fonctionner pour les nouvelles inscriptions. Les comptes existants ne sont pas affectés."}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={working}>Annuler</AlertDialogCancel>
            <AlertDialogAction onClick={confirmAction} disabled={working}>
              {working ? "En cours…" : "Confirmer"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/* Temp password display (AC-5.2) — shown once for the admin to relay */}
      <Dialog open={tempPassword !== null} onOpenChange={(open) => !open && setTempPassword(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Mot de passe temporaire</DialogTitle>
            <DialogDescription>
              Communiquez-le à {tempPassword?.email || "l'utilisateur"}. Il n'est affiché qu'une seule fois et
              l'utilisateur devra le changer à la prochaine connexion.
            </DialogDescription>
          </DialogHeader>
          <div className="flex items-center gap-2 rounded-lg border bg-muted p-3">
            <code className="flex-1 font-mono text-lg font-bold tracking-wider">{tempPassword?.password}</code>
            <Button variant="outline" size="sm" className="gap-1" onClick={copyTempPassword}>
              {copied ? <Check className="h-4 w-4" /> : <Copy className="h-4 w-4" />}
              {copied ? "Copié" : "Copier"}
            </Button>
          </div>
          <DialogFooter>
            <Button onClick={() => setTempPassword(null)}>Terminé</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
