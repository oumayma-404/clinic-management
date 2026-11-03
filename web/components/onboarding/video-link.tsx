import { PlayCircle } from "lucide-react"

import { Button } from "@/components/ui/button"
import { quoteFr } from "@/lib/format"
import { onboardingVideoUrl, type OnboardingVideo } from "@/lib/onboarding-videos"
import { cn } from "@/lib/utils"

/**
 * « Voir la vidéo · 0:53 » — opens one « Premiers pas » video on YouTube, in the system browser inside a shell.
 * Renders nothing for a video not filmed yet, rather than a link to nowhere.
 */
export function VideoLink({
  video,
  variant = "ghost",
  className,
}: {
  video: OnboardingVideo
  variant?: "ghost" | "outline" | "link"
  className?: string
}) {
  const url = onboardingVideoUrl(video)
  if (!url) return null

  return (
    <Button asChild variant={variant} className={cn("min-h-11", className)}>
      <a href={url} target="_blank" rel="noopener noreferrer">
        <PlayCircle aria-hidden />
        Voir la vidéo{video.length ? ` · ${video.length}` : ""}
        <span className="sr-only">{` ${quoteFr(video.title)}, s'ouvre sur YouTube`}</span>
      </a>
    </Button>
  )
}
