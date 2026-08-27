"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { toast } from "sonner"
import { Card, CardAction, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Label } from "@/components/ui/label"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Input } from "@/components/ui/input"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { statusToneClass } from "@/components/ui/status-tone"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { DEFAULT_PAGE_SIZE, emptyPage } from "@/lib/api/paging"
// The seven English storage keys + their French labels — the same list the setup and join wizards offer, so a
// practitioner created here is filed under a value every other screen already renders.
import { DOCTOR_SPECIALTIES, specialtyLabel } from "@/lib/specialties"
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
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Users, KeyRound, UserX, UserCheck, UserPlus, RefreshCw, Copy, Check, MoreHorizontal } from "lucide-react"
import { ZONES, zoneChipClass } from "@/lib/zones"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import {
  usersApi,
  USER_ROLES,
  USER_ROLE_LABELS_FR,
  type ClinicUserDto,
  type ClinicUsersPageDto,
  type UserRole,
} from "@/lib/api/users"
import { clinicsApi } from "@/lib/api/clinics"
import { authApi } from "@/lib/api/auth"
import { ApiError } from "@/lib/api/client"
import { useSession } from "@/lib/auth/session"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"

/** The chip both section headers wear. `/users` is the `config` zone — see the note at « Code de la clinique ». */
const SECTION_CHIP = `flex size-8 shrink-0 items-center justify-center rounded-lg ${zoneChipClass(ZONES.config)}`

type PendingAction =
  | { type: "reset"; user: ClinicUserDto }
  | { type: "status"; user: ClinicUserDto }
  | { type: "regenerate" }
  | { type: "role"; user: ClinicUserDto; role: UserRole }

export function UserManagement() {
  const { user: currentUser, mode } = useSession()
  /**
   * AC-P2.29: « Réinitialiser le mot de passe » only exists for password-backed accounts —
   * `ResetUserPasswordCommand` refuses anything else with « Ce compte n'utilise pas de mot de passe local. »
   *
   * ⚠️ Was `mode === "local"` and is unconditionally true now: the deployment kind that outsourced identity
   * (Auth0) is retired, and the « les mots de passe sont gérés par le fournisseur d'identité » note that used to
   * sit above this table went with it. Kept as a named constant rather than inlined, because it is a real
   * question a third deployment kind could answer differently — and if one ever does, it has one place to change.
   */
  const canResetPasswords = true
  /**
   * US-3: same underlying question as `canResetPasswords` — does this product own its accounts — asked
   * separately because the two are genuinely independent of the *third* one below. `POST /api/users` stays open
   * on both surviving profiles, since it is precisely what a deployment with self-registration closed uses
   * instead. (It 404s where `UsesLocalAccounts` is false, which no shipped profile now is.)
   */
  const canCreateAccounts = mode === "local"
  /**
   * Whether staff can still mint their own account with the clinic code. False on the hosted profile, where the
   * six-character code is a password everyone who ever worked at the practice knows. Asked of the server because
   * `mode` cannot answer it (US-3). Optimistic default: the LAN install is the common case, and a probe that
   * failed must not make « Code de la clinique » claim the code is dead when it is not.
   */
  const [selfRegistrationEnabled, setSelfRegistrationEnabled] = useState(true)
  const [userPage, setUserPage] = useState<ClinicUsersPageDto>(() => ({
    ...emptyPage<ClinicUserDto>(),
    pendingActivationCount: 0,
  }))
  const users = userPage.items
  /**
   * I5: self-registration no longer mints a live account, so somebody may be unable to log in right now and the
   * only place that says so is this screen. Counted server-side over the WHOLE clinic and deliberately not
   * narrowed by the search box — an admin who typed a name must still learn that someone else is waiting.
   */
  const pendingCount = userPage.pendingActivationCount
  const [search, setSearch] = useState("")
  const [debouncedSearch, setDebouncedSearch] = useState("")
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [clinicCode, setClinicCode] = useState<string>("")
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [pending, setPending] = useState<PendingAction | null>(null)
  const [working, setWorking] = useState(false)

  // Temp password shown once after a reset (AC-5.2) or after creating an account (US-3) — the same dialog,
  // because it is the same fact with the same handling rules.
  const [tempPassword, setTempPassword] = useState<{ email?: string; password: string } | null>(null)
  const [copied, setCopied] = useState(false)

  // « Créer un compte » (US-3).
  const [createOpen, setCreateOpen] = useState(false)
  const [createEmail, setCreateEmail] = useState("")
  const [createFullName, setCreateFullName] = useState("")
  const [createRole, setCreateRole] = useState<UserRole>("secretary")
  // The praticien fields, sent only for the doctor role. Kept when the admin switches role and back, so a mistaken
  // change of « Rôle » does not silently discard what they typed.
  const [createFirstName, setCreateFirstName] = useState("")
  const [createLastName, setCreateLastName] = useState("")
  const [createSpecialty, setCreateSpecialty] = useState<string>(DOCTOR_SPECIALTIES[0])
  const [createError, setCreateError] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)

  // Guards against setState after unmount (the admin can navigate away mid-load or mid-refresh).
  const mountedRef = useRef(true)
  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
    }
  }, [])

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search.trim()), 300)
    return () => clearTimeout(timer)
  }, [search])

  // A new term must not leave the table on a page the narrowed result set no longer has.
  useEffect(() => {
    setPage(1)
  }, [debouncedSearch])

  const loadData = useCallback(async () => {
    try {
      setLoading(true)
      setError(null)
      const [userList, status] = await Promise.all([
        usersApi.listPaged({ page, pageSize, search: debouncedSearch || undefined }),
        clinicsApi.getUserStatus(),
      ])
      if (!mountedRef.current) return
      setUserPage(userList)
      setClinicCode(status.clinic?.code || "")
    } catch (err) {
      if (!mountedRef.current) return
      const message = err instanceof ApiError ? err.message : "Échec du chargement des utilisateurs."
      setError(message)
    } finally {
      if (mountedRef.current) setLoading(false)
    }
  }, [page, pageSize, debouncedSearch])

  useEffect(() => {
    loadData()
  }, [loadData])

  // A deployment fact, so read once rather than on every page change.
  useEffect(() => {
    authApi
      .getMode()
      .then(({ selfRegistrationEnabled: enabled }) => {
        if (mountedRef.current) setSelfRegistrationEnabled(enabled)
      })
      .catch((err) => {
        // Deliberately no user-facing failure state, and this is not the `.catch(() => [])` shape: nothing is
        // emptied. The only consumer is the clinic-code caption, so an unread probe leaves it at the wording it
        // has always had — a claim the screen was already making — rather than inventing a new one either way.
        console.error("Could not read the deployment's auth capabilities:", err)
      })
  }, [])

  // Real-time: refetch the users table + clinic code when any client of this clinic changes a user
  // (reset password / activate / deactivate) or registers.
  useClinicRealtime(RealtimeResource.Users, loadData)

  /**
   * Band B — the row's version as the server holds it *now*, for a per-row action. The rendered row is as old as
   * the last refetch, and two admins on this screen changing the same colleague's rôle would otherwise silently
   * overwrite each other. Falls back to the rendered row's own token when the re-read fails: a stale token still
   * produces a 409 the admin can act on, whereas refusing the action on a network blip would be worse.
   */
  const currentVersion = async (user: ClinicUserDto): Promise<number> => {
    // Searched by email / name, which is what the server matches — an id is not a search term here.
    const term = user.email?.trim() || user.fullName?.trim()
    if (!term) return user.version
    try {
      const page = await usersApi.listPaged({ search: term })
      return page.items.find((u) => u.id === user.id)?.version ?? user.version
    } catch {
      return user.version
    }
  }

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
        const wasPending = pending.user.isPendingActivation
        await usersApi.setStatus(pending.user.id, nextActive, await currentVersion(pending.user))
        toast.success(
          nextActive
            ? wasPending
              ? "Compte activé. La personne peut maintenant se connecter."
              : "Utilisateur réactivé."
            : "Utilisateur désactivé.",
        )
        await loadData()
      } else if (pending.type === "regenerate") {
        const clinic = await clinicsApi.regenerateCode()
        setClinicCode(clinic.code || "")
        toast.success("Code de la clinique régénéré. L'ancien code ne fonctionne plus.")
      } else if (pending.type === "role") {
        await usersApi.setRole(pending.user.id, pending.role, await currentVersion(pending.user))
        toast.success(
          `Rôle modifié : ${USER_ROLE_LABELS_FR[pending.role]}. Il s'applique à la prochaine requête de l'utilisateur.`,
        )
        await loadData()
      }
      setPending(null)
    } catch (err) {
      const message = err instanceof ApiError ? err.message : "L'action a échoué. Veuillez réessayer."
      toast.error(message)
    } finally {
      setWorking(false)
    }
  }

  const submitCreate = async () => {
    setCreating(true)
    setCreateError(null)
    try {
      const created = await usersApi.create({
        email: createEmail.trim(),
        fullName: createFullName.trim(),
        role: createRole,
        // Only for the practitioner role — the server ignores it otherwise, and sending a half-filled object for a
        // secretary would be a doctor record nobody asked for.
        ...(createRole === "doctor"
          ? {
              doctorInfo: {
                firstName: createFirstName.trim(),
                lastName: createLastName.trim(),
                specialty: createSpecialty,
              },
            }
          : {}),
      })
      setCreateOpen(false)
      setCreateEmail("")
      setCreateFullName("")
      setCreateRole("secretary")
      setCreateFirstName("")
      setCreateLastName("")
      setCreateSpecialty(DOCTOR_SPECIALTIES[0])
      setTempPassword({ email: created.email, password: created.temporaryPassword })
      toast.success("Compte créé. Communiquez le mot de passe temporaire à la personne concernée.")
      await loadData()
    } catch (err) {
      // § 13: the dialog stays open with every field as typed — a duplicate email is corrected here, not retyped.
      setCreateError(err instanceof ApiError ? err.message : "La création du compte a échoué. Veuillez réessayer.")
    } finally {
      setCreating(false)
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

  // A role outside the closed set can only be legacy data, so render it verbatim rather than blank.
  const roleLabel = (role: string) =>
    USER_ROLE_LABELS_FR[role?.toLowerCase() as UserRole] ?? (role.charAt(0).toUpperCase() + role.slice(1))

  /** The stored role as one of the three known keys, or null for a legacy/unknown value. */
  const knownRole = (role: string): UserRole | null => {
    const key = role?.toLowerCase() as UserRole
    return USER_ROLES.includes(key) ? key : null
  }
  const formatDate = (value?: string) =>
    value ? new Date(value).toLocaleString("fr-FR") : "Jamais"

  // `min-h-full`, not `min-h-screen` — see the note in `clinic-settings.tsx`: inside `<main>` a full-viewport
  // minimum is always taller than the container, so it guaranteed an unnecessary scroll and trailing dead space.
  return (
    <div>
      {/* Same as `clinic-settings.tsx`: the hand-rolled header is gone because `/users` now renders
          `<PageHeader title="Utilisateurs">`, and its solid-primary `Users` mark duplicated the route's own
          page chip. `p-4` went with it — `AppShell` owns the gutter. */}
      <div className="mx-auto max-w-5xl space-y-4">

        {/*
          Clinic code + regenerate (AC-4.5) — and ONLY where the code still does something.

          ⚠️ US-3 originally kept this card on a hosted deployment and re-captioned it, on the reasoning that
          « the code is still the clinic's ». It is not: nothing in the product reads `ClinicCode` except the
          join path, so where self-registration is closed the card was a badge nobody can use, under a paragraph
          explaining why it does not work, beside a « Régénérer » button that rotates a value with no consumer.
          Three pieces of UI whose combined message is « ignore this » — which is more confusing than an absent
          card, not less, and it invites an admin to go hunting for the door it describes.

          Where the code IS live (SelfHostedLan) nothing changes: it is the normal way staff get an account.

          ⚠️ Gated on the enabled flag, never on its negation, because `selfRegistrationEnabled` defaults to
          **true** and a failed probe leaves it there (see its declaration). Hiding on failure would let a
          network blip silently remove the only way into the product on a LAN install.
        */}
        {selfRegistrationEnabled && (
        <Card>
          <CardHeader className="pb-3">
            {/*
              The icon chip — `app/documents/page.tsx`'s template-tile idiom, sized for a header: the glyph goes
              inside a tinted `rounded-lg` square rather than loose in the heading's own ink, where it would be
              more text rather than a mark the eye can find. `config` is this page's zone (`lib/zones.ts`), and
              it is the deliberately near-neutral one, so two of these down a page stay quiet.

              ⚠️ The `Users` glyph repeats the page title's own chip above. That is accepted rather than worked
              around: the two are different objects — the page mark is a solid `bg-primary` square with an
              inverted glyph, the section marks are washes — and inventing a second-choice glyph for the users
              list purely to avoid the repetition would make the *section* harder to recognise, which is the
              only thing the chip is for. The honest fix is in the page mark, which hand-rolls its header
              instead of using `ui/page-header.tsx` + `navIconForPath`: the rail draws `UserCog` for `/users`,
              and that helper exists precisely so a page never shows one icon while the rail shows another.
            */}
            <CardTitle className="flex min-w-0 items-center gap-2.5 text-base leading-snug">
              <span aria-hidden="true" className={SECTION_CHIP}>
                <KeyRound className="size-4" strokeWidth={1.75} />
              </span>
              Code de la clinique
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <Label className="text-xs text-muted-foreground">
                  À communiquer au personnel pour créer un compte
                </Label>
                <div className="mt-1.5">
                  {clinicCode ? (
                    <Badge
                      variant="outline"
                      className="border-primary/40 bg-card px-3 py-1 font-mono text-base font-bold text-primary"
                    >
                      {clinicCode}
                    </Badge>
                  ) : error ? (
                    // Band C — the code is read by the SAME failed call. « Aucun code défini » would be a claim
                    // about the cabinet made out of a network failure, and an admin would go and regenerate one.
                    <span className="text-sm text-muted-foreground">Code non chargé</span>
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
        )}

        {/* Users list (AC-5.1) */}
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="flex min-w-0 flex-wrap items-center gap-2.5 text-base leading-snug">
              <span aria-hidden="true" className={SECTION_CHIP}>
                <Users className="size-4" strokeWidth={1.75} />
              </span>
              Utilisateurs
              <Badge variant="secondary">{userPage.totalCount}</Badge>
              {/* `active` is the tone that asks for attention — see `ui/status-tone.ts`; there is no
                  separate "warning" tone and inventing a seventh colour is how the four private palettes
                  it replaced came about. */}
              {pendingCount > 0 && (
                <Badge variant="secondary" className={statusToneClass("active")}>
                  {pendingCount} en attente
                </Badge>
              )}
            </CardTitle>
            {/* `CardAction`, not `ms-auto` inside the title (which re-solved what `CardHeader`'s
                `has-data-[slot=card-action]:grid-cols-[1fr_auto]` already provides). Inside the wrapping title row the
                ~149 px button dropped to a second line where `ms-auto` right-aligned it alone and pushed the pending
                badge to a third — a three-line header with the action floating in the middle of it.

                ⚠️ `w-full sm:w-auto` on the button, and `sm:justify-self-end` on the action cell. At 320 px the
                153 px button and the icon chip shared the FIRST line while « Utilisateurs » wrapped to the second
                and the count badge sat alone on a third — 116 px of header in which the action appeared *above*
                the heading it belongs to, which is the exact shape the paragraph above says `CardAction` was
                introduced to avoid. Below `sm:` it now takes its own full-width row under the title; from `sm:` up
                nothing changes. */}
            {canCreateAccounts && (
              <CardAction className="col-span-full w-full sm:col-span-1 sm:w-auto sm:justify-self-end">
                <Button
                  size="sm"
                  className="w-full gap-2 sm:w-auto"
                  onClick={() => {
                    setCreateError(null)
                    setCreateOpen(true)
                  }}
                >
                  <UserPlus className="size-4" aria-hidden="true" />
                  Créer un compte
                </Button>
              </CardAction>
            )}
          </CardHeader>
          <CardContent>
            {/*
              Band C — a failed read is a FAILURE, not an empty clinic. This used to be a `FormErrorBanner` with no
              retry sitting ABOVE a table that went on rendering « Utilisateurs 0 » and its empty state, so a 500
              on this screen read as « ce cabinet n'a aucun utilisateur » — on the screen whose whole job is to say
              who can get in.
            */}
            {error && !loading && (
              <LoadFailureNotice
                className="mb-4"
                message="La liste des utilisateurs n'a pas pu être chargée."
                detail="Rien n'indique ici combien de comptes existent tant qu'elle n'est pas lue."
                onRetry={loadData}
              />
            )}
            {/*
              I5 — the one thing this screen has to say out loud.

              A self-registered account is created pending, so a new colleague who typed the clinic code cannot
              log in until somebody here presses « Réactiver ». They have no way to tell an admin through the
              product, and the row that needs approving may be on another page — so a badge on the row is not
              enough. The banner is stated in the plural-aware French the count needs and names the action.
            */}
            {pendingCount > 0 && (
              <p
                role="status"
                className="mb-4 rounded-md border border-warning/30 bg-warning-wash px-3 py-2 text-sm text-warning-ink"
              >
                {pendingCount === 1
                  ? "1 compte attend votre activation : cette personne s'est inscrite avec le code du cabinet mais ne peut pas encore se connecter."
                  : `${pendingCount} comptes attendent votre activation : ces personnes se sont inscrites avec le code du cabinet mais ne peuvent pas encore se connecter.`}{" "}
                Utilisez « Réactiver » sur la ligne concernée pour lui donner accès.
              </p>
            )}
            <div className="mb-4">
              <Label htmlFor="users-search" className="sr-only">
                Rechercher un utilisateur
              </Label>
              <Input
                id="users-search"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Rechercher un utilisateur (nom, email)…"
              />
            </div>
            {loading ? (
              <p className="py-8 text-center text-muted-foreground">Chargement des utilisateurs…</p>
            ) : error ? null : (
              <div className="overflow-x-auto">
                {/*
                  Two things this surface does differently.

                  (a) The title falls back to the email. `fullName` renders as "-" in the table when blank,
                      but a card titled "-" identifies nobody — and the email is what actually names the
                      account.
                  (b) The rôle `Select` stays a CONTROL, as the value of a labelled field. It is an action
                      living in a data column, so moving it into the menu would turn a one-tap change into
                      two and lose the current value from view; keeping it as a field keeps both.
                */}
                <CardList
                  className={CARDS_ONLY}
                  ariaLabel="Utilisateurs de la clinique"
                  items={users}
                  getKey={(u) => u.id}
                  title={(u) => u.fullName || u.email || "Compte sans nom"}
                  subtitle={(u) => (u.fullName ? u.email : null)}
                  muted={(u) => !u.isActive}
                  status={(u) => (
                    <>
                      {/*
                        Both halves of the pair go through `statusToneClass` (see the table below for the
                        same change). « Actif » was `bg-green-600 hover:bg-green-600` — a solid, *button*-
                        coloured pill with a hover state it never uses, on a span nobody can click — while
                        « Inactif » was the solid destructive variant. Two solids of unequal loudness for
                        two values of one field; the tones give both the same shape.
                      */}
                      {/* I5: three states, not two. « Inactif » on a five-minute-old self-registration reads as
                          a bug in the registration the person just completed; « En attente » says an approval is
                          owed and by whom. `active` is the attention tone, `negative` stays for a real shutdown. */}
                      {u.isActive ? (
                        <Badge variant="secondary" className={statusToneClass("positive")}>Actif</Badge>
                      ) : u.isPendingActivation ? (
                        <Badge variant="secondary" className={statusToneClass("active")}>En attente d&apos;activation</Badge>
                      ) : (
                        <Badge variant="secondary" className={statusToneClass("negative")}>Inactif</Badge>
                      )}
                      {u.mustChangePassword && (
                        <Badge variant="secondary" className="text-2xs">Doit changer le mot de passe</Badge>
                      )}
                    </>
                  )}
                  fields={(u) => [
                    {
                      label: "Rôle",
                      value: knownRole(u.role) ? (
                        <Select
                          value={knownRole(u.role)!}
                          onValueChange={(value) => setPending({ type: "role", user: u, role: value as UserRole })}
                          disabled={working}
                        >
                          {/* Card side: a card field is label-left / value-right, so a fixed 150px control
                              plus the « Rôle » label plus the gap overruns a 288px card. Full width here;
                              the desktop table below keeps its 150px column. Also 44px, not 32 — this is a
                              real control a secretary changes, not a read-out. */}
                          <SelectTrigger className="min-h-11 w-full text-sm" aria-label="Rôle">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            {USER_ROLES.map((role) => (
                              <SelectItem key={role} value={role} className="text-sm">
                                {USER_ROLE_LABELS_FR[role]}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      ) : (
                        <Badge variant="outline">{roleLabel(u.role)}</Badge>
                      ),
                    },
                    { label: "Dernière connexion", value: formatDate(u.lastLoginAt) },
                  ]}
                  actions={(u) => {
                    const isSelf = !!u.email && u.email === currentUser?.email
                    return (
                      <DropdownMenu>
                        <DropdownMenuTrigger asChild>
                          <Button variant="ghost" size="icon" aria-label={`Actions pour ${u.fullName || u.email}`}>
                            <MoreHorizontal className="h-4 w-4" />
                          </Button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end">
                          {canResetPasswords && (
                            <DropdownMenuItem onSelect={() => setPending({ type: "reset", user: u })}>
                              Réinitialiser le mot de passe
                            </DropdownMenuItem>
                          )}
                          {u.isActive ? (
                            <DropdownMenuItem
                              className="text-destructive focus:text-destructive"
                              disabled={isSelf}
                              onSelect={() => setPending({ type: "status", user: u })}
                            >
                              Désactiver
                            </DropdownMenuItem>
                          ) : (
                            <DropdownMenuItem onSelect={() => setPending({ type: "status", user: u })}>
                              {/* « Réactiver » is wrong for an account that was never active. */}
                              {u.isPendingActivation ? "Activer le compte" : "Réactiver"}
                            </DropdownMenuItem>
                          )}
                        </DropdownMenuContent>
                      </DropdownMenu>
                    )
                  }}
                  empty={
                    debouncedSearch ? "Aucun utilisateur ne correspond à votre recherche" : "Aucun utilisateur"
                  }
                />
                <Table containerClassName={TABLE_ONLY}>
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
                          {debouncedSearch
                            ? "Aucun utilisateur ne correspond à votre recherche"
                            : "Aucun utilisateur"}
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
                            {/* AC-P2.23 — no user's role could ever be changed, so a member onboarded with the
                                wrong one had to be deactivated and re-registered. A legacy value outside the
                                closed set keeps the read-only badge: pre-selecting a role we did not store
                                would misreport what the account actually holds. */}
                            {knownRole(user.role) ? (
                              <Select
                                value={knownRole(user.role)!}
                                onValueChange={(value) =>
                                  setPending({ type: "role", user, role: value as UserRole })
                                }
                                disabled={working}
                              >
                                <SelectTrigger className="h-8 w-[150px] text-sm" aria-label="Rôle">
                                  <SelectValue />
                                </SelectTrigger>
                                <SelectContent>
                                  {USER_ROLES.map((role) => (
                                    <SelectItem key={role} value={role} className="text-sm">
                                      {USER_ROLE_LABELS_FR[role]}
                                    </SelectItem>
                                  ))}
                                </SelectContent>
                              </Select>
                            ) : (
                              <Badge variant="outline">{roleLabel(user.role)}</Badge>
                            )}
                          </TableCell>
                          <TableCell>
                            <div className="flex flex-wrap items-center gap-1.5">
                              {user.isActive ? (
                                <Badge variant="secondary" className={statusToneClass("positive")}>Actif</Badge>
                              ) : user.isPendingActivation ? (
                                <Badge variant="secondary" className={statusToneClass("active")}>
                                  En attente d&apos;activation
                                </Badge>
                              ) : (
                                <Badge variant="secondary" className={statusToneClass("negative")}>Inactif</Badge>
                              )}
                              {user.mustChangePassword && (
                                <Badge variant="secondary" className="text-2xs">Doit changer le mot de passe</Badge>
                              )}
                            </div>
                          </TableCell>
                          <TableCell className="text-muted-foreground">{formatDate(user.lastLoginAt)}</TableCell>
                          <TableCell className="text-right">
                            <div className="flex justify-end gap-2">
                              {canResetPasswords && (
                                <Button
                                  variant="ghost"
                                  size="sm"
                                  className="h-8 gap-1"
                                  onClick={() => setPending({ type: "reset", user })}
                                >
                                  <KeyRound className="h-3 w-3" />
                                  Réinitialiser le mot de passe
                                </Button>
                              )}
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
                                  {user.isPendingActivation ? "Activer le compte" : "Réactiver"}
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
                <DataTablePagination
                  page={userPage}
                  onPageChange={setPage}
                  onPageSizeChange={setPageSize}
                  loading={loading}
                  label={["utilisateur", "utilisateurs"]}
                />
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
                (pending.user.isActive
                  ? "Désactiver cet utilisateur ?"
                  : pending.user.isPendingActivation
                    ? "Activer ce compte ?"
                    : "Réactiver cet utilisateur ?")}
              {pending?.type === "regenerate" && "Régénérer le code de la clinique ?"}
              {pending?.type === "role" && "Modifier le rôle de cet utilisateur ?"}
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
              {pending?.type === "status" && !pending.user.isActive && pending.user.isPendingActivation && (
                <>
                  {/* I5 — this is an access grant, not a restoration. Naming the role is the point: it is what
                      the person chose for themselves at registration, and it decides what they can see. */}
                  <span className="font-semibold">{pending.user.email}</span> s&apos;est inscrit(e) avec le code
                  du cabinet et pourra se connecter en tant que{" "}
                  <span className="font-semibold">{USER_ROLE_LABELS_FR[pending.user.role as UserRole] ?? pending.user.role}</span>.
                  Vérifiez qu&apos;il s&apos;agit bien d&apos;un membre de votre équipe : le compte donne accès aux
                  dossiers des patients.
                </>
              )}
              {pending?.type === "status" && !pending.user.isActive && !pending.user.isPendingActivation && (
                <>
                  <span className="font-semibold">{pending.user.email}</span> pourra de nouveau se connecter.
                </>
              )}
              {pending?.type === "regenerate" &&
                "Le code actuel cessera de fonctionner pour les nouvelles inscriptions. Les comptes existants ne sont pas affectés."}
              {pending?.type === "role" && (
                <>
                  {/*
                    ⚠️ It states the DESTINATION rôle only. « passera de X à Y » quoted the role as this tab last
                    read it, and from a stale tab that is a role the account no longer holds: with another admin
                    having just made them a doctor, the dialog said « passera de Secrétaire à Administrateur » and
                    the admin confirmed a sentence that was false. On this screen the sentence confirmed IS the
                    record of what the admin thinks they are doing, so it must only assert what is knowable here.
                    The stale save itself is now refused with a 409 (the version round-trip).
                  */}
                  <span className="font-semibold">{pending.user.email || pending.user.fullName}</span> aura le rôle{" "}
                  <span className="font-semibold">{USER_ROLE_LABELS_FR[pending.role]}</span>. Sa session actuelle
                  est révoquée : le nouveau rôle s&apos;applique dès sa prochaine requête, et il devra peut-être
                  se reconnecter.
                </>
              )}
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

      {/*
        « Créer un compte » (US-3). No password field, deliberately: the server mints one, so an admin cannot
        set a weak shared secret, and the account is forced to replace it at first login. Not a `Sheet` below
        `md:` — three short fields is a light surface, not a data-entry one (§ 5).
      */}
      <Dialog open={createOpen} onOpenChange={(open) => !creating && setCreateOpen(open)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Créer un compte</DialogTitle>
            <DialogDescription>
              Le mot de passe est généré et affiché une seule fois. La personne devra le changer à sa première
              connexion.
            </DialogDescription>
          </DialogHeader>
          <FormErrorBanner message={createError} />
          <div className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="create-user-fullname">Nom complet</Label>
              <Input
                id="create-user-fullname"
                value={createFullName}
                onChange={(e) => setCreateFullName(e.target.value)}
                autoComplete="off"
                disabled={creating}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="create-user-email">Email</Label>
              <Input
                id="create-user-email"
                type="email"
                value={createEmail}
                onChange={(e) => setCreateEmail(e.target.value)}
                autoComplete="off"
                disabled={creating}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="create-user-role">Rôle</Label>
              <Select
                value={createRole}
                onValueChange={(value) => setCreateRole(value as UserRole)}
                disabled={creating}
              >
                {/* `w-full` because the primitive's base is `w-fit`: without it this renders as a ~110 px stub in a
                    240 px column of full-width fields, and being shrink-to-fit it *changes width* when the admin
                    picks « Administrateur » over « Secrétaire », reflowing the field mid-interaction. */}
                <SelectTrigger id="create-user-role" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {USER_ROLES.map((role) => (
                    <SelectItem key={role} value={role}>
                      {USER_ROLE_LABELS_FR[role]}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">
                Le compte donne accès aux dossiers des patients. Le rôle décide de ce qu&apos;il peut consulter.
              </p>
            </div>

            {/*
              The practitioner behind a « médecin » account. Required by the server for that role and shown only for
              it, exactly as « Rejoindre une clinique » does — an admin or a secretary is not a practitioner.
              Without these the account got no `Doctor` record, so the person was absent from the praticien lists,
              « Mon profil » had nothing to edit, the money they collected was unattributed, and their ordonnances
              and certificats printed with **no** cachet and no n° d'ordre CNOMDT.
            */}
            {createRole === "doctor" && (
              <div className="space-y-4 rounded-lg border bg-muted/40 p-3">
                <p className="text-xs text-muted-foreground">
                  Ces informations créent la fiche praticien : elles apparaissent sur les ordonnances, les
                  certificats et les bulletins CNAM.
                </p>
                <div className="grid gap-4 sm:grid-cols-2">
                  <div className="space-y-2">
                    <Label htmlFor="create-user-firstname">Prénom du praticien</Label>
                    <Input
                      id="create-user-firstname"
                      value={createFirstName}
                      onChange={(e) => setCreateFirstName(e.target.value)}
                      autoComplete="off"
                      disabled={creating}
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="create-user-lastname">Nom du praticien</Label>
                    <Input
                      id="create-user-lastname"
                      value={createLastName}
                      onChange={(e) => setCreateLastName(e.target.value)}
                      autoComplete="off"
                      disabled={creating}
                    />
                  </div>
                </div>
                <div className="space-y-2">
                  <Label htmlFor="create-user-specialty">Spécialité</Label>
                  <Select
                    value={createSpecialty}
                    onValueChange={setCreateSpecialty}
                    disabled={creating}
                  >
                    <SelectTrigger id="create-user-specialty" className="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {DOCTOR_SPECIALTIES.map((specialty) => (
                        <SelectItem key={specialty} value={specialty}>
                          {specialtyLabel(specialty)}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
              </div>
            )}
          </div>
          {/* `coarse:h-11` — the default Button is `h-9` (36 px), under the § 2 floor on a finger. */}
          <DialogFooter>
            <Button
              variant="outline"
              className="coarse:h-11"
              onClick={() => setCreateOpen(false)}
              disabled={creating}
            >
              Annuler
            </Button>
            {/* The praticien terms are part of the gate for the doctor role, so the server's refusal is unreachable
                rather than merely unlikely — the same reasoning as `chequePaymentFields()`. */}
            <Button
              className="coarse:h-11"
              onClick={submitCreate}
              disabled={
                creating ||
                !createEmail.trim() ||
                !createFullName.trim() ||
                (createRole === "doctor" &&
                  (!createFirstName.trim() || !createLastName.trim() || !createSpecialty))
              }
            >
              {creating ? "Création…" : "Créer le compte"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Temp password display (AC-5.2) — shown once for the admin to relay */}
      <Dialog open={tempPassword !== null} onOpenChange={(open) => !open && setTempPassword(null)}>
        {/*
          `onInteractOutside` prevented because this dialog is *opened by* `submitCreate` in the same commit that
          closes « Créer un compte » — i.e. while the admin's thumb is still travelling from the button they just
          pressed, which on a phone sat where this sheet's content now lands. Below `md:` it renders as a ~300 px
          bottom sheet under ~540 px of live overlay, and Radix closes on a tap anywhere in that band. Escape, the ✕
          and « Terminé » stay as deliberate exits, so § 2's keyboard requirement is untouched.
        */}
        <DialogContent onInteractOutside={(event) => event.preventDefault()}>
          <DialogHeader>
            <DialogTitle>Mot de passe temporaire</DialogTitle>
            {/*
              The copy no longer denies a recovery that exists: `usersApi.resetPassword` is gated on the same
              condition as account creation, so « Réinitialiser le mot de passe » on the new row does regenerate one.
              Saying « affiché une seule fois » alone made a lost password read as an unrecoverable mistake.
            */}
            <DialogDescription>
              Communiquez-le à {tempPassword?.email || "l'utilisateur"}. Il n'est affiché qu'une seule fois et
              l'utilisateur devra le changer à la prochaine connexion. Si vous le perdez, utilisez
              « Réinitialiser le mot de passe » sur la ligne de ce compte pour en générer un nouveau.
            </DialogDescription>
          </DialogHeader>
          <div className="flex items-center gap-2 rounded-lg border bg-muted p-3">
            {/* `min-w-0` because a flex item's automatic minimum is its min-content width, and a monospace
                password has no break opportunity — without it the string cannot shrink and pushes « Copier »
                out of the dialog. `break-all` lets it wrap rather than overflow once it can shrink. */}
            <code className="min-w-0 flex-1 break-all font-mono text-lg font-bold tracking-wider">
              {tempPassword?.password}
            </code>
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
