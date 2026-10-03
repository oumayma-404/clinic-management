"use client"

import { AccessDeniedCard } from "@/components/ui/access-denied-card"

/** What a Finances page renders while « Mode discret » is on — one wording for the three pages. */
export function MoneyHiddenCard() {
  return <AccessDeniedCard title="Section masquée" description="Cette section est masquée pour le moment." />
}
