import * as React from "react"

import { cn } from "@/lib/utils"

export interface TextareaProps
  extends React.TextareaHTMLAttributes<HTMLTextAreaElement> {}

const Textarea = React.forwardRef<HTMLTextAreaElement, TextareaProps>(
  ({ className, ...props }, ref) => {
    return (
      <textarea
        className={cn(
          // `text-base` below `md:` (AC-12). iOS Safari zooms the page when a field smaller than 16px takes
          // focus, and it does not zoom back — the user is left on a magnified, sideways-scrolling page.
          // `Input` already carried this guard; `Textarea` did not, so every notes field had the defect.
          "flex min-h-[80px] w-full rounded-md border border-input bg-card px-3 py-2 text-base md:text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50",
          className
        )}
        ref={ref}
        {...props}
      />
    )
  }
)
Textarea.displayName = "Textarea"

export { Textarea }











