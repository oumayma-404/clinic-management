import { appointmentGuide } from "./appointment"
import { congesGuide } from "./conges"
import { dentalRecordGuide } from "./dental-record"
import { letterheadGuide } from "./letterhead"
import { patientsGuide } from "./patients"
import { pointageGuide } from "./pointage"
import { secretaryGuide } from "./secretary"
import type { Guide, GuideKey } from "./types"

export type { Guide, GuideKey, GuideStep, GuideAdvance } from "./types"

/** Every guide, by key — exhaustive, so a new key is a `tsc` error until its guide exists. */
export const GUIDES: Record<GuideKey, Guide> = {
  secretary: secretaryGuide,
  patients: patientsGuide,
  appointment: appointmentGuide,
  dentalRecord: dentalRecordGuide,
  letterhead: letterheadGuide,
  pointage: pointageGuide,
  conges: congesGuide,
}

/** On `<body>` while a guide runs: the post-visit question and the week-one card wait on it. */
export const GUIDE_RUNNING_ATTR = "data-guide-running"
