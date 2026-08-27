"use client"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import ClinicSettings from "@/components/clinic-settings"

export default function SettingsPage() {
  return (
    <ClinicGuard>
      {/*
        The shell's default width and gutter, like every other page.

        `width="none" gutter={false}` made this the one screen with no width limit at all — and it is the densest
        form in the app, so on a 1920px monitor its surface ran edge to edge while every list capped at
        `max-w-7xl`. It also left the route with **no page title**: `ClinicSettings` opens on a 16px `CardTitle`,
        which was the largest text on the screen, and the zone eyebrow/icon never appeared.

        ⚠️ Residual, and it needs a file this change does not own: `components/clinic-settings.tsx` still opens
        with `min-h-full bg-gray-50 dark:bg-slate-950` around its own `mx-auto max-w-5xl p-3`. Inside the shell's
        wrapper that grey surface becomes an inset slab and its `p-3` sits on top of the shell's `p-4 md:p-6`.
        The fix there is to delete that wrapper entirely and let the page own the layout.
      */}
      <AppShell contentClassName="space-y-6">
        <PageHeader
          title="Paramètres du cabinet"
          subtitle="Identité du cabinet, praticiens, horaires, facturation et rappels."
        />
        <ClinicSettings />
      </AppShell>
    </ClinicGuard>
  )
}

