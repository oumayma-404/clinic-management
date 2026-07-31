"use client"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import ClinicSettings from "@/components/clinic-settings"

export default function SettingsPage() {
  return (
    <ClinicGuard>
      {/* No gutter and no width wrapper (DEV-1): `ClinicSettings` paints its own full-bleed background and
          centres its own `max-w-5xl` at `p-3`. Adding the shell's would double-pad it, nest one max-width in
          another, and break its `min-h-full`, which resolves against `<main>`. The exemption is these two
          props rather than a hand-rolled `<main>`. */}
      <AppShell width="none" gutter={false}>
        <ClinicSettings />
      </AppShell>
    </ClinicGuard>
  )
}

