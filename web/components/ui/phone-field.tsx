"use client"

import { useEffect, useRef, useState } from "react"
import { Check } from "lucide-react"
import type { CountryCode } from "libphonenumber-js/max"
import { Button } from "@/components/ui/button"
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui/command"
import { Input } from "@/components/ui/input"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { cn } from "@/lib/utils"
import { quoteFr } from "@/lib/format"
import { COARSE_POINTER_QUERY } from "@/lib/hooks/use-media-query"
import {
  PHONE_COUNTRIES,
  countrySearchValue,
  regionOf,
  type PhoneCountry,
} from "@/lib/phone"

/**
 * A phone number and the country it belongs to, as one field.
 *
 * <p><b>Why a country control at all.</b> A number typed without a `+` cannot be resolved without knowing the
 * country — `06 12 34 56 78` is French, Italian or nothing depending on who typed it — so the rule needs a
 * default region and the user is the only one who can supply it. Every product that does this well pairs a
 * country selector with the field rather than asking a receptionist to type `+33`.</p>
 *
 * <p>⚠️ <b>The country is a control, not a derived value.</b> It seeds itself from a stored number
 * (`regionOf`) and follows a pasted `+33…`, but it is never *recomputed* from a half-typed one — a user who
 * picks France and then types four digits must not watch the selector snap back to Tunisia between keystrokes.
 * Changing it also never touches what has been typed, and typing never resets it: they are one field's two
 * halves (AC-9).</p>
 *
 * <p><b>A `Popover` + `Command`, following `ui/category-combobox.tsx`</b> — the repo's single-select combobox,
 * which already ships inside this feature's own supplier dialog. ⚠️ Deliberately <i>not</i>
 * `record/act-catalog-picker.tsx`'s inline `Command`: that one renders inline because it is a multi-select that
 * owns Enter for « add this act », and three components claiming Enter inside a dialog is the defect its
 * docstring records. Picking one country and closing has no such contest, and the search field's Enter is
 * consumed by cmdk before any form sees it.</p>
 */
interface PhoneFieldProps {
  /** The number as typed. Stored and submitted verbatim — this component never rewrites it. */
  value: string
  onChange: (value: string) => void
  /** The country a number with no country code is read as. */
  country: CountryCode
  onCountryChange: (country: CountryCode) => void
  /** Applied to the number input, so a `<Label htmlFor>` reaches the thing you type in. */
  id?: string
  placeholder?: string
  disabled?: boolean
  invalid?: boolean
  /**
   * Defaults to `"tel"`. ⚠️ Pass `"off"` for a number that is **not the form subject's** — an emergency
   * contact's, say: a `tel` suggestion there is the browser confidently writing the wrong person's number into
   * a clinical record.
   */
  autoComplete?: "tel" | "off"
  /** Forwarded to the number input for the field-summary banners some forms render. */
  "aria-describedby"?: string
  className?: string
}

export function PhoneField({
  value,
  onChange,
  country,
  onCountryChange,
  id,
  placeholder,
  disabled,
  invalid,
  autoComplete = "tel",
  className,
  ...rest
}: PhoneFieldProps) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState("")
  const listRef = useRef<HTMLDivElement>(null)

  const selected: PhoneCountry =
    PHONE_COUNTRIES.find((c) => c.code === country) ?? PHONE_COUNTRIES[0]

  /*
   * AC-11 — a number carrying its own country code moves the selector to that country, so the control never
   * disagrees with the field beside it. Keyed on the resolved region rather than on `value`, so it fires once
   * when a paste lands and not on every keystroke of a number being typed out.
   */
  const resolved = regionOf(value, country)
  useEffect(() => {
    if (resolved && resolved !== country) onCountryChange(resolved)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [resolved])

  const choose = (next: PhoneCountry) => {
    onCountryChange(next.code)
    setQuery("")
    setOpen(false)
  }

  return (
    /*
     * ⚠️ **One row, never wrapped.** The country and the number are one field and must read as one — stacking
     * them was tried and rejected on sight: it reads as two unrelated controls.
     *
     * That makes the trigger's width the whole problem, because the tightest cell in the product is the
     * fournisseur dialog's — a 2-column grid inside a 512 px dialog gives this row **223 px at 1440 px**, and a
     * desktop is therefore *narrower* here than a 320 px phone, where the grid has already collapsed. The first
     * version spent 117 px on the trigger (flag + indicatif + a chevron) and left the input 99 px against the
     * 124 px its own placeholder needed, so « ex. : 71 234 567 » rendered as « ex. : 71 234 ». The fix is to
     * spend less: no chevron — `role="combobox"` + `aria-expanded` already say it opens, and the bordered
     * flag-plus-indicatif reads as a control — which buys the number ~20 px and keeps both on one line.
     */
    <div className={cn("flex items-start gap-2", className)}>
      {/*
       * ⚠️ `modal` is load-bearing and must not be removed: without it the country list CANNOT BE SCROLLED WITH
       * A MOUSE WHEEL, only by dragging its scrollbar — reported from real use.
       *
       * A modal `Dialog` installs `react-remove-scroll`, which cancels wheel events everywhere outside the one
       * subtree it is given. `PopoverContent` portals to `document.body`, i.e. outside the dialog, so its list
       * is outside that subtree and every wheel over it is `preventDefault`ed. Measured with the picker open
       * inside the fournisseur dialog: `scrollHeight` 7848, `clientHeight` 260, wheel cancelled, `scrollTop`
       * pinned at 0. Dragging the scrollbar kept working, which is what makes this so easy to miss — that path
       * is browser chrome and never reaches JS.
       *
       * `modal` makes Radix register the popover's own content as an allowed shard, so the wheel survives.
       * Verified with a real trusted wheel (0 → 400 → 200), plus that Escape still closes only the popover, the
       * dialog stays open and typable, and neither the scroll lock nor `pointer-events: none` leaks afterwards.
       */}
      <Popover open={open} onOpenChange={setOpen} modal>
        <PopoverTrigger asChild>
          <Button
            type="button"
            variant="outline"
            role="combobox"
            aria-expanded={open}
            disabled={disabled}
            /* Grown, not `.touch-target`: it sits a gap away from the input, and an overlay would reach over
               it — the later sibling paints last, so a tap meant for the number would open this instead. */
            className="h-9 shrink-0 bg-card px-2.5 font-normal coarse:h-11"
            aria-label={`Pays du numéro : ${selected.name} (+${selected.callingCode})`}
          >
            <span aria-hidden="true" className="me-1.5 text-base leading-none">
              {flagOf(selected.code)}
            </span>
            <span className="text-sm tabular-nums">+{selected.callingCode}</span>
          </Button>
        </PopoverTrigger>
        <PopoverContent
          className="w-[min(20rem,calc(100vw-2rem))] p-0"
          align="start"
          onOpenAutoFocus={(event) => {
            /*
             * AC-10 — do not raise the keyboard on a finger. Radix focuses the first focusable child, which is
             * the search box, so on a coarse pointer the list opens under a keyboard covering half of it. The
             * JS twin of `coarse:` is the only way to know: this is a decision about a prop, not a class.
             */
            if (window.matchMedia?.(COARSE_POINTER_QUERY).matches) {
              event.preventDefault()
              listRef.current?.focus()
            }
          }}
        >
          <Command>
            <CommandInput
              placeholder="Pays, indicatif…"
              value={query}
              onValueChange={setQuery}
            />
            {/* A rem-free px cap, so `sheet-vh` does not fire and the list keeps a usable height with a
                keyboard open. ~250 rows render unvirtualised and cmdk hides the non-matches — the same order
                of magnitude as the act catalogue this app already renders that way. */}
            <CommandList ref={listRef} className="max-h-[260px]">
              <CommandEmpty>Aucun pays pour {quoteFr(query.trim())}.</CommandEmpty>
              <CommandGroup>
                {PHONE_COUNTRIES.map((c) => (
                  <CommandItem
                    key={c.code}
                    /* « France », « FR », « 33 » and « +33 » all reach France — cmdk matches on `value`, not
                       on what is rendered (AC-7). */
                    value={countrySearchValue(c)}
                    className="coarse:py-3"
                    onSelect={() => choose(c)}
                  >
                    <Check
                      aria-hidden="true"
                      className={cn("me-2 size-4 shrink-0", c.code === country ? "opacity-100" : "opacity-0")}
                    />
                    <span aria-hidden="true" className="me-2 shrink-0 text-base leading-none">
                      {flagOf(c.code)}
                    </span>
                    {/* The name is text, never carried by the flag alone — a glyph that does not render (and
                        on Windows a regional-indicator pair does not) would leave the row unreadable. */}
                    <span className="min-w-0 flex-1 truncate">{c.name}</span>
                    <span className="ms-2 shrink-0 text-2xs tabular-nums text-muted-foreground">
                      +{c.callingCode}
                    </span>
                  </CommandItem>
                ))}
              </CommandGroup>
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>

      <Input
        id={id}
        type="tel"
        inputMode="tel"
        autoComplete={autoComplete}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        disabled={disabled}
        aria-invalid={invalid || undefined}
        className={cn("min-w-0 flex-1", invalid && "border-destructive")}
        {...rest}
      />
    </div>
  )
}

/**
 * The flag emoji for an ISO country code, built from regional-indicator code points.
 *
 * ⚠️ Decorative in every use here — Windows ships no flag glyphs and renders the pair as the two letters
 * instead, which is why every row and the trigger also carry real text.
 */
function flagOf(code: string): string {
  return code
    .toUpperCase()
    .replace(/./g, (c) => String.fromCodePoint(0x1f1a5 + c.charCodeAt(0)))
}
