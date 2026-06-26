"use client"

import { useState, useEffect } from "react"
import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"

type ToothStatus = {
  id: string
  worked: boolean
  procedures: Array<{
    type: string
    notes: string
    date: string
  }>
}

type DentalChartProps = {
  onTeethChange?: (selectedTeeth: ToothStatus[]) => void
  initialData?: ToothStatus[]
  onTeethTypeChange?: (isAdult: boolean) => void
  readOnly?: boolean
  defaultIsAdult?: boolean // For read-only mode, set which chart to show
}

// Adult teeth: 32 teeth (quadrants 1-4)
const ADULT_TEETH = {
  upperRight: [18, 17, 16, 15, 14, 13, 12, 11], // Quadrant 1
  upperLeft: [21, 22, 23, 24, 25, 26, 27, 28], // Quadrant 2
  lowerLeft: [31, 32, 33, 34, 35, 36, 37, 38], // Quadrant 3 - reversed
  lowerRight: [48, 47, 46, 45, 44, 43, 42, 41], // Quadrant 4 - reversed
}

// Child teeth: 20 teeth (quadrants 5-8)
const CHILD_TEETH = {
  upperRight: [55, 54, 53, 52, 51], // Quadrant 5
  upperLeft: [61, 62, 63, 64, 65], // Quadrant 6
  lowerLeft: [71, 72, 73, 74, 75], // Quadrant 7 - reversed
  lowerRight: [85, 84, 83, 82, 81], // Quadrant 8 - reversed
}

export function DentalChart({ onTeethChange, initialData = [], onTeethTypeChange, readOnly = false, defaultIsAdult = true }: DentalChartProps) {
  const [isAdult, setIsAdult] = useState(defaultIsAdult)
  const [selectedTeeth, setSelectedTeeth] = useState<Set<string>>(
    new Set(initialData.filter((t) => t.worked).map((t) => t.id)),
  )
  const [toothData, setToothData] = useState<Map<string, ToothStatus>>(new Map(initialData.map((t) => [t.id, t])))
  
  // Update selected teeth when initialData changes (for read-only mode)
  useEffect(() => {
    if (readOnly) {
      setSelectedTeeth(new Set(initialData.filter((t) => t.worked).map((t) => t.id)))
      setToothData(new Map(initialData.map((t) => [t.id, t])))
      setIsAdult(defaultIsAdult)
    }
  }, [initialData, readOnly, defaultIsAdult])

  const teeth = isAdult ? ADULT_TEETH : CHILD_TEETH

  const toggleTooth = (toothId: string) => {
    if (readOnly) return // Don't allow changes in read-only mode
    
    const newSelected = new Set(selectedTeeth)
    if (newSelected.has(toothId)) {
      newSelected.delete(toothId)
    } else {
      newSelected.add(toothId)
    }
    setSelectedTeeth(newSelected)
    
    // Notify parent of changes immediately
    const result: ToothStatus[] = Array.from(newSelected).map((id) => {
      const existing = toothData.get(id)
      return (
        existing || {
          id,
          worked: true,
          procedures: [],
        }
      )
    })
    onTeethChange?.(result)
  }

  return (
    <div className="w-full max-w-full space-y-3 overflow-hidden overflow-x-hidden">
      {/* Toggle between adult and child teeth */}
      {!readOnly && (
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <span className="text-xs font-medium text-muted-foreground">Chart:</span>
            <div className="flex items-center gap-1 bg-muted rounded-lg p-1">
              <Button
                variant={isAdult ? "default" : "ghost"}
                size="sm"
                onClick={() => {
                  setIsAdult(true)
                  setSelectedTeeth(new Set())
                  onTeethTypeChange?.(true)
                  onTeethChange?.([]) // Notify parent of cleared selection
                }}
                className="h-7 text-xs px-2"
              >
                Adult
              </Button>
              <Button
                variant={!isAdult ? "default" : "ghost"}
                size="sm"
                onClick={() => {
                  setIsAdult(false)
                  setSelectedTeeth(new Set())
                  onTeethTypeChange?.(false)
                  onTeethChange?.([]) // Notify parent of cleared selection
                }}
                className="h-7 text-xs px-2"
              >
                Child
              </Button>
            </div>
          </div>
          {selectedTeeth.size > 0 && (
            <span className="text-xs text-muted-foreground">
              {selectedTeeth.size} {selectedTeeth.size === 1 ? "tooth" : "teeth"} selected
            </span>
          )}
        </div>
      )}
      {readOnly && (
        <div className="flex items-center justify-between">
          <span className="text-xs font-medium text-muted-foreground">
            {isAdult ? "Adult Teeth" : "Child Teeth"} - Read Only
          </span>
          {selectedTeeth.size > 0 && (
            <span className="text-xs text-muted-foreground">
              {selectedTeeth.size} {selectedTeeth.size === 1 ? "tooth" : "teeth"} worked on
            </span>
          )}
        </div>
      )}

      {/* Dental Chart */}
      <div className="border border-border rounded-lg p-2 bg-card overflow-x-hidden">
        {/* Upper Jaw */}
        <div className="space-y-1.5">
          <div className="text-center text-[10px] font-medium text-muted-foreground">Upper Jaw</div>
          <div className="flex justify-center gap-2">
            {/* Upper Right */}
            <div className="flex gap-0.5">
              {teeth.upperRight.map((toothNum) => (
                <Tooth
                  key={toothNum}
                  number={toothNum}
                  isSelected={selectedTeeth.has(String(toothNum))}
                  onClick={() => toggleTooth(String(toothNum))}
                />
              ))}
            </div>
            {/* Center line */}
            <div className="w-px bg-border" />
            {/* Upper Left */}
            <div className="flex gap-0.5">
              {teeth.upperLeft.map((toothNum) => (
                <Tooth
                  key={toothNum}
                  number={toothNum}
                  isSelected={selectedTeeth.has(String(toothNum))}
                  onClick={() => toggleTooth(String(toothNum))}
                />
              ))}
            </div>
          </div>
        </div>

        {/* Divider */}
        <div className="my-2 border-t border-border" />

        {/* Lower Jaw */}
        <div className="space-y-1.5">
          <div className="flex justify-center gap-2">
            {/* Lower Right */}
            <div className="flex gap-0.5">
              {teeth.lowerRight.map((toothNum) => (
                <Tooth
                  key={toothNum}
                  number={toothNum}
                  isSelected={selectedTeeth.has(String(toothNum))}
                  onClick={() => toggleTooth(String(toothNum))}
                />
              ))}
            </div>
            {/* Center line */}
            <div className="w-px bg-border" />
            {/* Lower Left */}
            <div className="flex gap-0.5">
              {teeth.lowerLeft.map((toothNum) => (
                <Tooth
                  key={toothNum}
                  number={toothNum}
                  isSelected={selectedTeeth.has(String(toothNum))}
                  onClick={() => toggleTooth(String(toothNum))}
                />
              ))}
            </div>
          </div>
          <div className="text-center text-[10px] font-medium text-muted-foreground">Lower Jaw</div>
        </div>
      </div>

      {/* Legend */}
      <div className="flex items-center justify-center gap-4 text-xs">
        <div className="flex items-center gap-1.5">
          <div className="w-6 h-8 border border-border rounded bg-background" />
          <span className="text-muted-foreground">Not worked</span>
        </div>
        <div className="flex items-center gap-1.5">
          <div className="w-6 h-8 border border-blue-500 rounded bg-blue-500/20" />
          <span className="text-muted-foreground">Selected</span>
        </div>
      </div>
    </div>
  )
}

// Individual tooth component
function Tooth({
  number,
  isSelected,
  onClick,
}: {
  number: number
  isSelected: boolean
  onClick: () => void
}) {
  const getToothType = (num: number) => {
    const lastDigit = num % 10
    if (lastDigit === 1 || lastDigit === 2) return "incisor"
    if (lastDigit === 3) return "canine"
    if (lastDigit === 4 || lastDigit === 5) return "premolar"
    if (lastDigit === 6 || lastDigit === 7 || lastDigit === 8) return "molar"
    return "incisor"
  }

  const toothType = getToothType(number)

  return (
    <button
      onClick={onClick}
      className={cn(
        "group relative flex flex-col items-center justify-center transition-all hover:scale-105",
        "focus:outline-none focus:ring-1 focus:ring-ring rounded",
      )}
    >
      <div className="relative">
        {toothType === "incisor" && <IncisorTooth isSelected={isSelected} />}
        {toothType === "canine" && <CanineTooth isSelected={isSelected} />}
        {toothType === "premolar" && <PremolarTooth isSelected={isSelected} />}
        {toothType === "molar" && <MolarTooth isSelected={isSelected} />}

        {/* Selection indicator */}
        {isSelected && <div className="absolute inset-0 rounded border-2 border-blue-500 pointer-events-none" />}
      </div>

      {/* Tooth number below */}
      <span
        className={cn(
          "text-[9px] font-medium mt-0.5 transition-colors",
          isSelected ? "text-blue-700 dark:text-blue-400" : "text-muted-foreground group-hover:text-foreground",
        )}
      >
        {number}
      </span>
    </button>
  )
}

function IncisorTooth({ isSelected }: { isSelected: boolean }) {
  return (
    <svg
      width="16"
      height="28"
      viewBox="0 0 32 56"
      fill="none"
      className={cn("transition-all drop-shadow-sm", isSelected && "drop-shadow-lg")}
    >
      <path
        d="M16 2C12 2 9 4 7 7C5 10 4 14 4 18C4 28 4 38 6 44C8 50 11 54 16 54C21 54 24 50 26 44C28 38 28 28 28 18C28 14 27 10 25 7C23 4 20 2 16 2Z"
        className={cn(
          "transition-colors",
          isSelected
            ? "fill-blue-500 stroke-blue-600"
            : "fill-white dark:fill-gray-50 stroke-gray-400 group-hover:stroke-gray-500",
        )}
        strokeWidth="2"
      />
      {/* Incisor cutting edge */}
      <rect
        x="6"
        y="10"
        width="20"
        height="3"
        rx="1"
        className={cn("transition-opacity", isSelected ? "opacity-20 fill-blue-700" : "opacity-10 fill-gray-600")}
      />
    </svg>
  )
}

function CanineTooth({ isSelected }: { isSelected: boolean }) {
  return (
    <svg
      width="18"
      height="29"
      viewBox="0 0 36 58"
      fill="none"
      className={cn("transition-all drop-shadow-sm", isSelected && "drop-shadow-lg")}
    >
      <path
        d="M18 2C15 2 12 3 10 5C8 7 6 10 5 14C4 18 4 24 4 28C4 36 5 44 7 48C9 52 13 56 18 56C23 56 27 52 29 48C31 44 32 36 32 28C32 24 32 18 31 14C30 10 28 7 26 5C24 3 21 2 18 2Z"
        className={cn(
          "transition-colors",
          isSelected
            ? "fill-blue-500 stroke-blue-600"
            : "fill-white dark:fill-gray-50 stroke-gray-400 group-hover:stroke-gray-500",
        )}
        strokeWidth="2"
      />
      {/* Canine cusp - pointed tip */}
      <path
        d="M18 6L12 16L24 16Z"
        className={cn("transition-opacity", isSelected ? "opacity-20 fill-blue-700" : "opacity-10 fill-gray-600")}
      />
    </svg>
  )
}

function PremolarTooth({ isSelected }: { isSelected: boolean }) {
  return (
    <svg
      width="21"
      height="30"
      viewBox="0 0 42 60"
      fill="none"
      className={cn("transition-all drop-shadow-sm", isSelected && "drop-shadow-lg")}
    >
      <path
        d="M21 4C17 4 13 6 11 9C9 12 7 16 6 20C5 24 4 30 4 34C4 40 5 46 7 50C9 54 13 60 21 60C29 60 33 54 35 50C37 46 38 40 38 34C38 30 37 24 36 20C35 16 33 12 31 9C29 6 25 4 21 4Z"
        className={cn(
          "transition-colors",
          isSelected
            ? "fill-blue-500 stroke-blue-600"
            : "fill-white dark:fill-gray-50 stroke-gray-400 group-hover:stroke-gray-500",
        )}
        strokeWidth="2"
      />
      {/* Two cusps */}
      <circle
        cx="15"
        cy="16"
        r="4"
        className={cn("transition-opacity", isSelected ? "opacity-20 fill-blue-700" : "opacity-10 fill-gray-600")}
      />
      <circle
        cx="27"
        cy="16"
        r="4"
        className={cn("transition-opacity", isSelected ? "opacity-20 fill-blue-700" : "opacity-10 fill-gray-600")}
      />
    </svg>
  )
}

function MolarTooth({ isSelected }: { isSelected: boolean }) {
  return (
    <svg
      width="24"
      height="31"
      viewBox="0 0 48 62"
      fill="none"
      className={cn("transition-all drop-shadow-sm", isSelected && "drop-shadow-lg")}
    >
      <path
        d="M24 4C19 4 15 6 12 9C9 12 7 16 6 20C5 24 4 30 4 34C4 40 5 46 7 50C9 54 14 60 24 60C34 60 39 54 41 50C43 46 44 40 44 34C44 30 43 24 42 20C41 16 39 12 36 9C33 6 29 4 24 4Z"
        className={cn(
          "transition-colors",
          isSelected
            ? "fill-blue-500 stroke-blue-600"
            : "fill-white dark:fill-gray-50 stroke-gray-400 group-hover:stroke-gray-500",
        )}
        strokeWidth="2"
      />
      {/* Four cusps in quadrants */}
      <circle
        cx="16"
        cy="16"
        r="4"
        className={cn("transition-opacity", isSelected ? "opacity-20 fill-blue-700" : "opacity-10 fill-gray-600")}
      />
      <circle
        cx="32"
        cy="16"
        r="4"
        className={cn("transition-opacity", isSelected ? "opacity-20 fill-blue-700" : "opacity-10 fill-gray-600")}
      />
      <circle
        cx="16"
        cy="26"
        r="4"
        className={cn("transition-opacity", isSelected ? "opacity-20 fill-blue-700" : "opacity-10 fill-gray-600")}
      />
      <circle
        cx="32"
        cy="26"
        r="4"
        className={cn("transition-opacity", isSelected ? "opacity-20 fill-blue-700" : "opacity-10 fill-gray-600")}
      />
    </svg>
  )
}

