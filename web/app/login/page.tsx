"use client"

import { useEffect, useState } from 'react'
import { useRouter } from 'next/navigation'
import { useSession } from '@/lib/auth/session'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

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
      <div className="flex h-screen items-center justify-center">
        <p className="text-muted-foreground">Loading...</p>
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
    <div className="flex h-screen items-center justify-center bg-background">
      <Card className="w-full max-w-md">
        <CardHeader className="space-y-1">
          <CardTitle className="text-2xl font-bold">Welcome to MediCare Clinic</CardTitle>
          <CardDescription>
            Please sign in to access the clinic management system
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Button asChild className="w-full" size="lg">
            <a href="/auth/login">Sign In</a>
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

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setIsSubmitting(true)
    try {
      const res = await fetch('/api/auth/local-login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password }),
      })
      const data = await res.json().catch(() => null)
      if (!res.ok) {
        setError(data?.error || 'Invalid email or password.')
        setIsSubmitting(false)
        return
      }
      // Full navigation so the session cookie is picked up by middleware + providers.
      const returnTo = new URLSearchParams(window.location.search).get('returnTo')
      window.location.href = returnTo && returnTo.startsWith('/') ? returnTo : '/'
    } catch {
      setError('Cannot reach the clinic server. Please try again.')
      setIsSubmitting(false)
    }
  }

  return (
    <div className="flex h-screen items-center justify-center bg-background">
      <Card className="w-full max-w-md">
        <CardHeader className="space-y-1">
          <CardTitle className="text-2xl font-bold">Sign in</CardTitle>
          <CardDescription>Enter your clinic account credentials</CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSubmit} className="space-y-4">
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
              <Label htmlFor="password">Password</Label>
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
              {isSubmitting ? 'Signing in...' : 'Sign In'}
            </Button>
            <p className="text-center text-sm text-muted-foreground">
              Have a clinic code?{' '}
              <a href="/join" className="text-primary hover:underline">
                Create an account
              </a>
            </p>
            <p className="text-center text-sm text-muted-foreground">
              First time setting up this clinic?{' '}
              <a href="/setup" className="text-primary hover:underline">
                Set up the clinic
              </a>
            </p>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
