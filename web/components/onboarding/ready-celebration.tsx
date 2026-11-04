"use client"

import { useEffect, useState } from "react"

import { READY_EVENT, launchConfettiWaves } from "@/components/onboarding/step-celebration"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { GETTING_STARTED_READY_JOKE } from "@/lib/getting-started"

/**
 * « Bienvenue dans votre nouveau cabinet ! » (owner's call, 2026-11-04) — the end of « Premiers pas », celebrated in a
 * window nobody can miss: confetti in waves, one warm sentence, one button. Fun, still a professional's tool: no
 * sound, nothing to dismiss twice, and the window closes like any other (« C'est parti ! », ✕, Échap).
 * Mounted once (`GuideProvider`); `celebrateReady()` opens it from anywhere.
 */
export function ReadyCelebrationHost() {
  const [open, setOpen] = useState(false)

  useEffect(() => {
    const show = () => {
      setOpen(true)
      launchConfettiWaves()
    }
    window.addEventListener(READY_EVENT, show)
    return () => window.removeEventListener(READY_EVENT, show)
  }, [])

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogContent className="overflow-hidden text-center md:max-w-md">
        <div aria-hidden className="-mx-6 -mt-6 mb-2 bg-primary/10 py-6 text-6xl leading-none">
          🎉
        </div>
        {/* `md:text-center` + `pe-0`: the header's own `md:text-left pe-8` would pull a centred moment off-centre. */}
        <DialogHeader className="items-center pe-0 text-center md:text-center">
          <p className="text-xs font-semibold uppercase tracking-wide text-primary">Félicitations</p>
          <DialogTitle className="text-2xl leading-tight">Bienvenue dans votre nouveau cabinet !</DialogTitle>
          <DialogDescription className="text-base text-foreground">
            Les premiers pas sont faits. {GETTING_STARTED_READY_JOKE} Place aux sourires de vos patients.
          </DialogDescription>
        </DialogHeader>
        <Button type="button" size="lg" className="mx-auto mt-2 min-h-11 px-8" onClick={() => setOpen(false)}>
          C&apos;est parti !
        </Button>
      </DialogContent>
    </Dialog>
  )
}
