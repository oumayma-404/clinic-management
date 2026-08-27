"use client"

import { OTPInput, REGEXP_ONLY_DIGITS, type SlotProps } from 'input-otp'

import { Label } from '@/components/ui/label'
import { cn } from '@/lib/utils'

/** Every code this product can ever verify is six digits long — see the note on `TOTP_CODE_LENGTH` below. */
export const TOTP_CODE_LENGTH = 6

interface TotpCodeFieldProps {
  id?: string
  label?: string
  value: string
  onChange: (value: string) => void
  autoFocus?: boolean
  disabled?: boolean
  /** Rendered under the field. A `<p>`, so it can carry its own `id` for `aria-describedby`. */
  hint?: string
  /**
   * Fired once the sixth digit lands, however it arrived — typed, pasted, or filled by the OS. Callers use it to
   * submit without a click; they must still guard their own in-flight state, because a paste over an existing
   * value can complete the code twice.
   */
  onComplete?: () => void
}

/**
 * The one-time-code field (`hosted-security-hardening` FR-1.2, step 19) — **six boxes over one real input**.
 *
 * ⚠️ **Six boxes, and the two objections that used to forbid them are answered by the mechanism, not waived.**
 * This renders `input-otp`, which paints fake slots over a **single** focusable `<input>` — so pasting « 123 456 »
 * and a password manager filling `one-time-code` both work exactly as they did with a plain field, because there
 * is still exactly one field to paste into and to fill. Six separate `<input>`s would break both, and that — not
 * the segmented look — was the real hazard. `input-otp` was already a dependency of this project.
 *
 * ⚠️ **The length is fixed at six because the server can verify nothing else.** `TotpService` builds `new Totp(key)`
 * on Otp.NET's defaults — SHA-1, **6 digits**, a 30-second step — and its own docstring says a deployment that
 * varied them would hand the operator's phone a secret it reads as permanently wrong. So an eight-digit
 * authenticator is not a configuration this product has; clamping to six truncates nothing that could have worked.
 * (Recovery codes are not this control — they are a plain text field, on the screens that take one.)
 *
 * ⚠️ **`type="text"` + `inputMode="numeric"`, never `type="number"`.** A number input *eats a leading zero* — and
 * one in six TOTP codes starts with one — so a user would be told their correct code is wrong, roughly every sixth
 * attempt, with nothing on screen to explain it.
 *
 * ⚠️ **The slots are `aria-hidden` and the input keeps the label.** A screen reader must hear one « Code de
 * vérification » field, not six unlabelled boxes; the visual segmentation is decoration over a single value.
 *
 * Non-digits are stripped as they arrive: authenticators display « 123 456 » and people copy the space with it.
 */
export function TotpCodeField({
  id = 'totp-code',
  label = 'Code de vérification',
  value,
  onChange,
  autoFocus,
  disabled,
  hint,
  onComplete,
}: TotpCodeFieldProps) {
  const hintId = hint ? `${id}-hint` : undefined

  return (
    <div className="space-y-2">
      <Label htmlFor={id}>{label}</Label>
      <OTPInput
        id={id}
        name={id}
        value={value}
        onChange={onChange}
        /*
         * ⚠️ **Wrapped, never passed through.** `input-otp` types `onComplete` as `(...args: any[]) => unknown`
         * and calls it *with* the value, so handing it a caller's callback silently widens this component's own
         * `() => void` contract — and a submit handler shaped `(e?: React.FormEvent)` then receives a string and
         * dies on `e.preventDefault is not a function`, at the exact moment the sixth digit lands. TypeScript
         * cannot see it: the extra argument is legal against `() => void`. Swallowing the args here makes the
         * declared signature true for every caller instead of for the ones that happen to ignore parameters.
         */
        onComplete={onComplete ? () => onComplete() : undefined}
        maxLength={TOTP_CODE_LENGTH}
        pattern={REGEXP_ONLY_DIGITS}
        inputMode="numeric"
        // What lets iOS and Android offer the code straight from the notification shade, which for most people is
        // how it gets entered at all.
        autoComplete="one-time-code"
        autoFocus={autoFocus}
        disabled={disabled}
        required
        aria-describedby={hintId}
        // A pasted « 123 456 » (or a code copied with a trailing newline) is the normal case, not an error.
        pasteTransformer={(pasted) => pasted.replace(/\D/g, '')}
        containerClassName={cn(
          // `gap-1` below `sm:`, deliberately: at 320 px the content box is 288 px, so six 44 px slots need every
          // pixel the gaps are not using. `flex-1` + `min-w-0` lets them share whatever is actually there.
          'flex items-center gap-1 sm:gap-2',
          disabled && 'opacity-50',
        )}
        render={({ slots }) => (
          <>
            {slots.map((slot, index) => (
              <Slot key={index} {...slot} />
            ))}
          </>
        )}
      />
      {hint && (
        <p id={hintId} className="text-sm text-muted-foreground">
          {hint}
        </p>
      )}
    </div>
  )
}

/**
 * One painted box. `aria-hidden` — the value it shows is already announced by the input underneath, and six
 * announced boxes would read a six-digit code as six separate fields.
 *
 * `h-12` (48 px) clears § 2's 44 px floor on every pointer, so it needs no `coarse:` variant: the whole strip is
 * one tap target anyway, and growing it only on touch would make the boxes jump between a tablet and a desk
 * machine showing the same page.
 */
function Slot({ char, isActive, hasFakeCaret }: SlotProps) {
  return (
    <div
      aria-hidden="true"
      className={cn(
        'relative flex h-12 min-w-0 flex-1 items-center justify-center rounded-md border border-input',
        'bg-transparent text-lg font-medium tabular-nums transition-[border-color,box-shadow] duration-[160ms]',
        isActive && 'z-10 border-ring ring-[3px] ring-ring/50',
      )}
    >
      {char}
      {hasFakeCaret && (
        <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
          <div className="h-5 w-px animate-pulse bg-foreground" />
        </div>
      )}
    </div>
  )
}
