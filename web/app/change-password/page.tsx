import { cookies } from 'next/headers'
import { ChangePasswordForm } from '@/components/change-password-form'
import { readMustChangeCookie } from '@/lib/auth/session-cookie'

export const dynamic = 'force-dynamic'

// Forced (post-reset) and voluntary password change (AC-5.2). Reads the forced-change flag
// server-side so the copy matches the situation; the form posts to /bff/auth/change-password.
export default async function ChangePasswordPage() {
  const cookieStore = await cookies()
  const forced = readMustChangeCookie((name) => cookieStore.get(name)?.value) === '1'

  return <ChangePasswordForm forced={forced} />
}
