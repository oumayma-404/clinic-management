"use client"

import { useEffect, useState } from 'react'
import { useRouter } from 'next/navigation'
import { useSession } from '@/lib/auth/session'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { PRODUCT_NAME } from '@/lib/brand'

export default function LoginPage() {
  const { user, isLoading, mode } = useSession()
  const router = useRouter()

  useEffect(() => {
    if (user && !isLoading) {
      // Cloud: go to /setup (redirects onward if a clinic exists). Local: go to the app.
      router.push(mode === 'local' ? '/' : '/setup')
    }
  }, [user, isLoading, mode, router])

  if (isLoading) {
    return (
      <div className="flex h-dvh items-center justify-center">
        <p className="text-muted-foreground">Chargement…</p>
      </div>
    )
  }

  if (user) {
    return null
  }

  if (mode === 'local') {
    return <LocalLoginForm />
  }

  return (
    <div className="flex h-dvh items-center justify-center bg-background">
      <Card className="w-full max-w-md">
        <CardHeader className="space-y-1">
          <CardTitle className="text-2xl font-bold">Bienvenue sur {PRODUCT_NAME}</CardTitle>
          <CardDescription>
            Connectez-vous pour accéder au système de gestion de la clinique
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Button asChild className="w-full" size="lg">
            <a href="/auth/login">Se connecter</a>
          </Button>
        </CardContent>
      </Card>
    </div>
  )
}

function LocalLoginForm() {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  // A successful password change revokes its own session (SetPassword bumps TokenVersion), so
  // /change-password lands here rather than in the app. Without this the user meets a bare login form
  // and cannot tell whether the change was saved — the one fact they came here to establish.
  const [passwordChanged, setPasswordChanged] = useState(false)

  useEffect(() => {
    setPasswordChanged(new URLSearchParams(window.location.search).get('passwordChanged') === '1')
  }, [])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setIsSubmitting(true)
    try {
      const res = await fetch('/bff/auth/local-login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password }),
      })
      const data = await res.json().catch(() => null)
      if (!res.ok) {
        setError(data?.error || 'Email ou mot de passe invalide.')
        setIsSubmitting(false)
        return
      }
      // Full navigation so the session cookie is picked up by middleware + providers.
      // Only allow same-origin absolute paths — reject protocol-relative ("//host") and
      // backslash ("/\\host") forms that browsers treat as off-site redirects.
      const returnTo = new URLSearchParams(window.location.search).get('returnTo')
      const isSafeReturnTo =
        !!returnTo && returnTo.startsWith('/') && !returnTo.startsWith('//') && !returnTo.startsWith('/\\')
      window.location.href = isSafeReturnTo ? returnTo! : '/'
    } catch {
      setError('Impossible de joindre le serveur de la clinique. Veuillez réessayer.')
      setIsSubmitting(false)
    }
  }

  return (
    <div className="flex h-dvh items-center justify-center bg-background">
      <Card className="w-full max-w-md">
        <CardHeader className="space-y-1">
          <CardTitle className="text-2xl font-bold">Connexion</CardTitle>
          <CardDescription>Saisissez les identifiants de votre compte clinique</CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSubmit} className="space-y-4">
            {passwordChanged && !error && (
              <div
                role="status"
                className="rounded-lg border border-success/20 bg-success-wash p-3 text-sm text-success"
              >
                Mot de passe enregistré. Connectez-vous avec votre nouveau mot de passe.
              </div>
            )}
            {error && (
              <div className="p-3 bg-destructive/10 border border-destructive/20 rounded-lg text-destructive text-sm">
                {error}
              </div>
            )}
            <div className="space-y-2">
              <Label htmlFor="email">Email</Label>
              <Input
                id="email"
                type="email"
                autoComplete="username"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="password">Mot de passe</Label>
              <Input
                id="password"
                type="password"
                autoComplete="current-password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
              />
            </div>
            <Button type="submit" className="w-full" size="lg" disabled={isSubmitting}>
              {isSubmitting ? 'Connexion…' : 'Se connecter'}
            </Button>
            <p className="text-center text-sm text-muted-foreground">
              Vous avez un code de clinique ?{' '}
              <a href="/join" className="text-primary hover:underline">
                Rejoindre la clinique
              </a>
            </p>
            <p className="text-center text-sm text-muted-foreground">
              Première configuration de cette clinique ?{' '}
              <a href="/setup" className="text-primary hover:underline">
                Configurer la clinique
              </a>
            </p>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
