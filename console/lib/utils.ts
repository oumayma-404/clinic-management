import { clsx, type ClassValue } from "clsx"
import { twMerge } from "tailwind-merge"

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

// Parse a backend TimeSpan string (e.g. "00:30:00") to minutes; defaults to 60 on a malformed value.
export function parseDurationToMinutes(duration: string): number {
  const parts = duration.split(":")
  if (parts.length === 3) {
    const hours = parseInt(parts[0], 10)
    const minutes = parseInt(parts[1], 10)
    return hours * 60 + minutes
  }
  return 60 // Default to 1 hour
}
