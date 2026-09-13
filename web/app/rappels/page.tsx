"use client"

import { useRouter } from "next/navigation"
import { ArrowLeft } from "lucide-react"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"

/**
 * « Rappels » is parked until a real SMS sender and a real WhatsApp number are contracted.
 *
 * <p>Not a retirement and not an error, which is why this is not {@link RetiredPageCard}: the screen is finished
 * and the server behind it is intact — there are simply no provider credentials yet, so every row the log could
 * show would be a message nothing ever sent. « Bientôt » is the honest sentence; a log that is permanently empty
 * is read as a product that loses messages.</p>
 *
 * <p><b>Nothing was removed</b> — the screen lives verbatim in `rappels-screen.tsx` beside this file, unrouted but
 * still compiled. Bringing it back is importing `RappelsScreen` and returning it here.</p>
 */
export default function RappelsPage() {
  const router = useRouter()

  return (
    <ClinicGuard>
      {/* `min-h-full` centring resolves against `<main>`, so the shell carries no width and no gutter. */}
      <AppShell width="none" gutter={false}>
        <div className="flex min-h-full items-center justify-center p-6">
          <Card className="w-full max-w-md">
            <CardHeader className="space-y-3 text-center">
              <p aria-hidden="true" className="text-6xl leading-none">
                ⏳
              </p>
              <CardTitle>Bientôt disponible</CardTitle>
              <CardDescription>
                Rincez, crachez… et revenez plus tard. Les rappels SMS et WhatsApp ne sont pas encore branchés.
              </CardDescription>
            </CardHeader>
            <CardContent>
              {/* `min-h-11` for the coarse-pointer floor — the only control on the screen. */}
              <Button
                variant="outline"
                className="min-h-11 w-full gap-2"
                onClick={() => router.push("/appointments")}
              >
                <ArrowLeft className="size-4" aria-hidden="true" />
                Retour à l&apos;agenda
              </Button>
            </CardContent>
          </Card>
        </div>
      </AppShell>
    </ClinicGuard>
  )
}
