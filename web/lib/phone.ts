// The phone rule, browser side — the mirror of the backend `PhoneNumber.ToE164`.
//
// ⚠️ This file is ONE OF TWO implementations of one rule, and the pair is held together by
// `shared/phone-e164-corpus.json`: a C# test and check:responsive's `phone-rule-matches-the-corpus` drive both
// sides from the same inputs and pin them to the same outputs. Before that corpus existed the only thing holding
// them together was a comment in each file saying the other was a mirror — and a mirror nobody checks is a
// mirror that is already wrong.
//
// It exists at all so a bad number is refused without a round trip. The SERVER is still the authority: it
// re-checks on every write, and `PatientDto.phoneE164` — not this file — is what decides whether a WhatsApp
// action appears on a row.

import {
  parsePhoneNumberFromString,
  getCountries,
  getCountryCallingCode,
  type CountryCode,
} from "libphonenumber-js/max"

/**
 * The country assumed when a number carries no country code.
 *
 * ⚠️ The one place that assumption is written on this side (AC-4); its twin is `PhoneNumber.DefaultRegion`, and
 * `shared/phone-e164-corpus.json` states the value both are held to. Tunisia because every cabinet is Tunisian —
 * the governorate list is 24 Tunisian names and the tax settings are Tunisian. A future per-clinic country
 * setting replaces this constant and nothing else.
 */
export const DEFAULT_REGION: CountryCode = "TN"

/**
 * Normalizes a phone to E.164, or `null` when no country can parse it.
 *
 * Any country is accepted. A number carrying its own country code (`+33 6 12 34 56 78`, `0033612345678`) is
 * parsed as that country's whatever `region` says; a bare national number is parsed as `region`'s, which is why
 * `20 123 456` is still `+21620123456` and why `06 12 34 56 78` needs the selector to say `FR`.
 *
 * ⚠️ Validity is per-country, and that is the point: `201234567` — a Tunisian number with a ninth digit — is
 * refused, where the hand-rolled rule this replaced could only count digits and would have had to accept it.
 */
export function toE164(raw: string | null | undefined, region: CountryCode = DEFAULT_REGION): string | null {
  if (!raw || !raw.trim()) return null
  try {
    const parsed = parsePhoneNumberFromString(raw, region)
    return parsed && parsed.isValid() ? parsed.number : null
  } catch {
    // The library throws on some malformed input rather than returning undefined. A refusal, not an error.
    return null
  }
}

/** True when `raw` is a number we can reach (see {@link toE164}). */
export function isDeliverablePhone(
  raw: string | null | undefined,
  region: CountryCode = DEFAULT_REGION,
): boolean {
  return toE164(raw, region) !== null
}

/**
 * The country `raw` belongs to, or `null` when it cannot be parsed.
 *
 * Lets a stored number re-open its own country in the selector, so the control never disagrees with the field
 * beside it (AC-11) — and lets a pasted `+33…` move the selector to France as the user types.
 */
export function regionOf(
  raw: string | null | undefined,
  region: CountryCode = DEFAULT_REGION,
): CountryCode | null {
  if (!raw || !raw.trim()) return null
  try {
    const parsed = parsePhoneNumberFromString(raw, region)
    return parsed && parsed.isValid() ? (parsed.country ?? null) : null
  } catch {
    return null
  }
}

/**
 * French inline error shown when a phone fails {@link isDeliverablePhone}.
 *
 * ⚠️ A **client-owned** string for the client-side pre-check, not a copy of the server's refusal — that one lives
 * in `PhoneRefusals.Invalid` and reaches the user verbatim through `getErrorMessage` when a write is refused.
 * Two sentences with two roles; neither restates the other, and there is deliberately no `code → French` table
 * (`graceful-error-handling` considered one and deferred it).
 *
 * It names no country, which is what the sentence it replaced got wrong: « Utilisez un numéro tunisien à 8
 * chiffres » became false the moment the rule widened, in seven places at once.
 */
export const PHONE_ERROR_FR =
  "Numéro de téléphone invalide. Choisissez le pays, ou saisissez le numéro au format international (+33…)."

/** One country as the selector renders it. */
export interface PhoneCountry {
  /** ISO 3166-1 alpha-2, e.g. `TN`. */
  code: CountryCode
  /** The country's name in French, e.g. « Tunisie ». */
  name: string
  /** The calling code without a `+`, e.g. `216`. */
  callingCode: string
}

/**
 * Every country the library knows, named in French and sorted by name.
 *
 * ⚠️ **The names come from `Intl.DisplayNames`, not from a list in this repo.** A hand-kept table of ~250 French
 * country names would be a second authority that ages — countries are renamed — and it is exactly the kind of
 * list this codebase has been bitten by keeping by hand. The browser already holds the CLDR data.
 *
 * Sorted with a French collator so « Éthiopie » lands under E and « Åland » under A, which a plain
 * `localeCompare` on the default locale does not guarantee. Computed once at module scope: it is ~250 entries and
 * never changes within a session, and rebuilding it per keystroke inside a picker is what makes a filter feel slow.
 */
export const PHONE_COUNTRIES: PhoneCountry[] = (() => {
  // `Intl.DisplayNames` is in every browser this app supports, but it throws on an unknown locale in some
  // engines and is absent in an old JSDOM — falling back to the ISO code keeps the picker usable rather than
  // taking the form down with it.
  let display: Intl.DisplayNames | null = null
  try {
    display = new Intl.DisplayNames(["fr"], { type: "region" })
  } catch {
    display = null
  }

  const collator = new Intl.Collator("fr", { sensitivity: "base" })

  return getCountries()
    .map((code) => ({
      code,
      name: display?.of(code) ?? code,
      callingCode: getCountryCallingCode(code),
    }))
    .sort((a, b) => collator.compare(a.name, b.name))
})()

/**
 * The haystack cmdk matches a country row against — French name, ISO code and calling code with and without its
 * `+`, so « France », « FR », « 33 » and « +33 » all find France (AC-7).
 *
 * The `+` variant is not redundant: a user who knows the number starts `+33` types the `+` first, and cmdk
 * matches on this string rather than on what is rendered.
 */
export function countrySearchValue(country: PhoneCountry): string {
  return `${country.name} ${country.code} ${country.callingCode} +${country.callingCode}`
}
