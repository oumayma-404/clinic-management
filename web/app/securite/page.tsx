"use client"

import { useCallback, useEffect, useState } from 'react'
import { AppShell } from '@/components/app-shell'
import { ClinicGuard } from '@/components/clinic-guard'
import { PageHeader } from '@/components/ui/page-header'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { TotpCodeField } from '@/components/security/totp-code-field'
import { RecoveryCodesPanel } from '@/components/security/recovery-codes-panel'
import { EmptyState } from '@/components/ui/empty-state'
import { LOW_RECOVERY_CODES, securityApi, type TotpState } from '@/lib/api/security'
import { showErrorToast } from '@/lib/errors'
import { formatDate } from '@/lib/format'
import { ShieldCheck, ShieldAlert, RefreshCw } from 'lucide-react'
import { toast } from 'sonner'

/**
 * « Sécurité » — this account's own second factor (`hosted-security-hardening` FR-1.5).
 *
 * ⚠️ **Reachable by every role**, and that is the point: a doctor or a secretary may enrol voluntarily on any
 * deployment, and this is the only screen where they can. It is deliberately not « Mon profil » (a
 * practitioner's *document* identity, which a secretary does not have) and not « Paramètres » (clinic-wide and
 * admin-shaped) — this is about one person's own credential.
 *
 * ⚠️ **An administrator who cannot disable theirs is told so in words**, with the control absent rather than
 * present-and-refusing. A disabled button with no explanation reads as a bug.
 */
export default function SecuritePage() {
  return (
    <ClinicGuard>
      <AppShell>
        <SecurityContent />
      </AppShell>
    </ClinicGuard>
  )
}

function SecurityContent() {
  const [state, setState] = useState<TotpState | null>(null)
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState<'regenerate' | 'disable' | null>(null)
  const [freshCodes, setFreshCodes] = useState<string[] | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    setFailed(false)
    try {
      setState(await securityApi.getTotpState())
    } catch {
      // A failed read is NOT « pas de second facteur » — those are opposite facts with the same picture (§ 13).
      setFailed(true)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const regenerate = async () => {
    setBusy('regenerate')
    try {
      const { recoveryCodes } = await securityApi.regenerateRecoveryCodes(code)
      setFreshCodes(recoveryCodes)
      setCode('')
      await load()
    } catch (err) {
      showErrorToast(err)
    } finally {
      setBusy(null)
    }
  }

  const disable = async () => {
    setBusy('disable')
    try {
      await securityApi.disableTotp(code)
      setCode('')
      toast.success('Second facteur désactivé.')
      await load()
    } catch (err) {
      showErrorToast(err)
    } finally {
      setBusy(null)
    }
  }

  return (
    <div className="space-y-6">
      {/* A fact, not a paraphrase of the screen (§ `page-header.tsx`): the state and the codes left are what
          somebody opens this page to learn. Absent while unknown, rather than guessed. */}
      <PageHeader
        title="Sécurité"
        subtitle={
          state
            ? state.isEnrolled
              ? `Second facteur activé · ${state.recoveryCodesRemaining} code${
                  (state.recoveryCodesRemaining ?? 0) > 1 ? 's' : ''
                } de récupération inutilisé${(state.recoveryCodesRemaining ?? 0) > 1 ? 's' : ''}`
              : 'Second facteur non activé'
            : undefined
        }
      />

      {loading && <Card><CardContent className="py-8"><p className="text-muted-foreground">Chargement…</p></CardContent></Card>}

      {!loading && failed && (
        <Card>
          <CardContent className="space-y-3 py-6">
            <p className="text-sm">Les informations de sécurité n&apos;ont pas pu être chargées.</p>
            <Button onClick={load} variant="outline" className="min-h-11">Réessayer</Button>
          </CardContent>
        </Card>
      )}

      {/* Shown once, right after regenerating. */}
      {freshCodes && (
        <Card>
          <CardHeader>
            <CardTitle>Nouveaux codes de récupération</CardTitle>
            <CardDescription>Les codes précédents ne fonctionnent plus.</CardDescription>
          </CardHeader>
          <CardContent>
            <RecoveryCodesPanel
              codes={freshCodes}
              confirmLabel="J'ai terminé"
              onConfirm={() => setFreshCodes(null)}
            />
          </CardContent>
        </Card>
      )}

      {!loading && !failed && state && !freshCodes && (
        <>
          <Card>
            <CardHeader className="flex flex-row items-start gap-3 space-y-0">
              <span
                aria-hidden
                className={`flex size-8 shrink-0 items-center justify-center rounded-lg ${
                  state.isEnrolled ? 'bg-success-wash text-success' : 'bg-warning-wash text-warning-ink'
                }`}
              >
                {state.isEnrolled ? <ShieldCheck className="size-4" /> : <ShieldAlert className="size-4" />}
              </span>
              <div className="min-w-0 space-y-1">
                <CardTitle className="text-lg">
                  {state.isEnrolled ? 'Second facteur activé' : 'Second facteur non activé'}
                </CardTitle>
                <CardDescription>
                  {state.isEnrolled
                    ? state.enrolledAt
                      ? `Activé le ${formatDate(state.enrolledAt)}.`
                      : 'Un code à usage unique est demandé à chaque connexion.'
                    : state.isRequired
                      ? 'Ce compte doit en activer un : il vous sera demandé à votre prochaine connexion.'
                      : 'Vous pouvez en activer un pour protéger votre compte au-delà du mot de passe.'}
                </CardDescription>
              </div>
              {state.isRequired && (
                <Badge variant="outline" className="ms-auto shrink-0">Obligatoire</Badge>
              )}
            </CardHeader>

            {!state.isEnrolled && (
              <CardContent>
                {/* Enrolment lives on the login screen: it must work for somebody who cannot get in at all,
                    so there is one implementation rather than two. */}
                <EmptyState
                  icon={ShieldCheck}
                  title="Activer le second facteur"
                  description="L'activation se fait depuis l'écran de connexion, où elle fonctionne aussi pour un compte qui ne peut pas encore se connecter."
                  size="compact"
                  action={
                    <Button asChild className="min-h-11">
                      <a href="/login">Aller à l&apos;écran de connexion</a>
                    </Button>
                  }
                />
              </CardContent>
            )}
          </Card>

          {state.isEnrolled && (
            <Card>
              <CardHeader>
                <CardTitle className="text-lg">Codes de récupération</CardTitle>
                <CardDescription>
                  {state.recoveryCodesRemaining} code{(state.recoveryCodesRemaining ?? 0) > 1 ? 's' : ''} inutilisé
                  {(state.recoveryCodesRemaining ?? 0) > 1 ? 's' : ''}.
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                {/* ⚠️ Above the « peu / aucun » warning, because it is the more urgent fact and the one with a
                    deadline: the window a redeemed code opened is the only way to move the factor off a lost
                    phone without an administrator or the vendor, and it lapses in minutes. Shown here because
                    somebody who dismissed the prompt on the login screen will come looking on this page. */}
                {state.mayReplace && (
                  <p
                    role="status"
                    className="rounded-lg border border-primary/30 bg-primary/5 p-3 text-sm"
                  >
                    Vous vous êtes connecté avec un code de récupération. Pendant quelques minutes, vous pouvez
                    lier votre second facteur à un nouveau téléphone sans code de vérification.{' '}
                    <a href="/login?replace=1" className="font-medium underline underline-offset-4">
                      Remplacer maintenant
                    </a>
                    .
                  </p>
                )}

                {/* ⚠️ The warning appears HERE and nowhere else — beside the control that resolves it. There is
                    deliberately no badge or banner on any other screen (Stated Assumption 7). */}
                {/* « peu » and « aucun » are different facts, and the second is the one that locks somebody out:
                    at zero, losing the authenticator means losing the account. Same box, sharper sentence. */}
                {(state.recoveryCodesRemaining ?? 0) <= LOW_RECOVERY_CODES && (
                  <p
                    role="status"
                    className="rounded-lg border border-warning/30 bg-warning-wash p-3 text-sm text-warning-ink"
                  >
                    {(state.recoveryCodesRemaining ?? 0) === 0
                      ? "Il ne vous reste aucun code de récupération : si vous perdez votre application d’authentification, vous perdrez l’accès à votre compte. Régénérez une nouvelle série dès maintenant."
                      : "Il vous reste peu de codes de récupération. Régénérez-en une nouvelle série pour ne pas risquer de perdre l’accès à votre compte."}
                  </p>
                )}

                <TotpCodeField
                  id="manage-totp-code"
                  label="Code de vérification"
                  value={code}
                  onChange={setCode}
                  disabled={busy !== null}
                  hint="Requis pour régénérer vos codes ou désactiver le second facteur."
                />

                <div className="flex flex-col gap-2 sm:flex-row">
                  <Button
                    onClick={regenerate}
                    disabled={busy !== null || code.length === 0}
                    className="min-h-11"
                  >
                    <RefreshCw aria-hidden className="size-4" />
                    {busy === 'regenerate' ? 'Régénération…' : 'Régénérer les codes'}
                  </Button>

                  {/* ⚠️ Absent, not disabled, for an admin the deployment obliges — with the reason stated
                      below, because a control that is simply missing reads as a bug. */}
                  {!state.isRequired && (
                    <Button
                      variant="outline"
                      onClick={disable}
                      disabled={busy !== null || code.length === 0}
                      className="min-h-11"
                    >
                      {busy === 'disable' ? 'Désactivation…' : 'Désactiver le second facteur'}
                    </Button>
                  )}
                </div>

                {state.isRequired && (
                  <p className="text-sm text-muted-foreground">
                    Le second facteur est obligatoire pour les administrateurs de cette installation : il ne peut
                    pas être désactivé. Pour le remplacer, demandez à un autre administrateur de le réinitialiser.
                  </p>
                )}
              </CardContent>
            </Card>
          )}
        </>
      )}
    </div>
  )
}
