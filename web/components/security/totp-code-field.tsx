"use client"

import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

interface TotpCodeFieldProps {
  id?: string
  label?: string
  value: string
  onChange: (value: string) => void
  autoFocus?: boolean
  disabled?: boolean
  /** Rendered under the field. A `<p>`, so it can carry its own `id` for `aria-describedby`. */
  hint?: string
}

/**
 * The one-time-code field (`hosted-security-hardening` FR-1.2, step 19).
 *
 * ⚠️ **One field, never six boxes.** A segmented input looks tidier and breaks the two ways this code actually
 * arrives: pasting it, and a password manager filling it. `input-otp` is installed in this project and is
 * deliberately not used here for that reason.
 *
 * ⚠️ **`type="text"` with `inputMode="numeric"`, never `type="number"`.** A number input *eats a leading zero* —
 * and one in six TOTP codes starts with one — so a user would be told their correct code is wrong, roughly every
 * sixth attempt, with nothing on screen to explain it. It also brings spinners and accepts `e`, `+` and `-`.
 *
 * ⚠️ **`autoComplete="one-time-code"`** is what lets iOS and Android offer the code from the notification
 * shade, which for most people is how it gets typed at all.
 *
 * Whitespace is stripped as it is typed: authenticators display « 123 456 » and people copy the space with it.
 */
export function TotpCodeField({
  id = 'totp-code',
  label = 'Code de vérification',
  value,
  onChange,
  autoFocus,
  disabled,
  hint,
}: TotpCodeFieldProps) {
  const hintId = hint ? `${id}-hint` : undefined

  return (
    <div className="space-y-2">
      <Label htmlFor={id}>{label}</Label>
      <Input
        id={id}
        name={id}
        type="text"
        inputMode="numeric"
        autoComplete="one-time-code"
        // Not `pattern`/`maxLength={6}`: a recovery-code flow reuses nothing here, but an authenticator set to
        // 8 digits is a real configuration and clamping to 6 would silently truncate it.
        autoFocus={autoFocus}
        disabled={disabled}
        required
        value={value}
        aria-describedby={hintId}
        onChange={(e) => onChange(e.target.value.replace(/\s/g, ''))}
      />
      {hint && (
        <p id={hintId} className="text-sm text-muted-foreground">
          {hint}
        </p>
      )}
    </div>
  )
}
