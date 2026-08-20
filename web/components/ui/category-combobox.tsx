"use client"

import { useMemo, useState } from "react"
import { Check, ChevronsUpDown, Plus } from "lucide-react"
import { Button } from "@/components/ui/button"
import {
  Command,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui/command"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { cn } from "@/lib/utils"

/**
 * A field over an **open set with suggestions** — pick one of the offered labels, or type your own.
 *
 * <p>This is the client half of the pattern `Domain/Services/CategoryFolding` owns server-side, and it now has
 * three fields in the product: an act's discipline, a fournisseur's catégorie and a stock article's catégorie.
 * The rule each of them depends on is that the offered list already contains what the clinic has used — so the
 * second person to want « Occlusodontie » <i>picks</i> it rather than retyping it, and an open set converges on
 * itself instead of shredding into near-duplicates.</p>
 *
 * <p><b>A combobox and not a `Select`</b>, deliberately: a Select cannot express « or something else », and the
 * fields are open precisely because a practice must not need a release to file work it already performs.</p>
 *
 * <p>⚠️ <b>Typing a variant of a known label is safe and needs no client-side check</b> — the server folds
 * « prothese » back onto « Prothèse / Laboratoire » on write. What this component prevents is the *other* half:
 * offering the same label twice, once as a suggestion and once as « Utiliser », which is why the create row is
 * hidden as soon as the typed text names an existing option.</p>
 *
 * <p>⚠️ <b>The search box's text is separate state from the value.</b> Typing must not change what is saved
 * until a row is chosen, or abandoning a half-typed word would silently refile the record.</p>
 *
 * <p>NOTE: `procedure-type-form-modal.tsx` still carries its own inline copy of this pattern. It predates this
 * component and works; folding it in is a follow-up rather than something this feature changed underneath it.</p>
 */
interface CategoryComboboxProps {
  value: string
  onChange: (value: string) => void
  /** The canonical suggestions unioned with the clinic's own, as served by the API. */
  options: string[]
  /** What the trigger reads when nothing is chosen. */
  placeholder?: string
  /** The « clear it » row's label. */
  emptyLabel?: string
  id?: string
  disabled?: boolean
}

export function CategoryCombobox({
  value,
  onChange,
  options,
  placeholder = "Sans catégorie",
  emptyLabel = "Sans catégorie",
  id,
  disabled,
}: CategoryComboboxProps) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState("")

  const typed = query.trim()
  const canCreate = useMemo(
    () => typed !== "" && !options.some((o) => o.toLowerCase() === typed.toLowerCase()),
    [typed, options],
  )

  const choose = (next: string) => {
    onChange(next)
    setQuery("")
    setOpen(false)
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          id={id}
          type="button"
          variant="outline"
          role="combobox"
          aria-expanded={open}
          disabled={disabled}
          className="w-full justify-between font-normal"
        >
          <span className={cn("truncate", !value && "text-muted-foreground")}>{value || placeholder}</span>
          <ChevronsUpDown aria-hidden="true" className="ms-2 size-4 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent
        className="p-0"
        align="start"
        style={{ width: "var(--radix-popover-trigger-width)" }}
      >
        <Command>
          {/* « ou créer », not « ou saisir » — the create row is the whole point of this component and nothing
              said it existed until you guessed you could type. Kept short so it survives a 320 px popover. */}
          <CommandInput
            placeholder="Rechercher ou créer…"
            value={query}
            onValueChange={setQuery}
          />
          <CommandList>
            {/* No CommandEmpty: with a query typed there is always the « Utiliser » row below, and with none
                there is always the clear row — so the list is never actually empty, and « Aucun résultat »
                would sit directly above a row that is a result. */}
            <CommandGroup>
              <CommandItem value="__aucune categorie__" className="coarse:py-3" onSelect={() => choose("")}>
                <Check
                  aria-hidden="true"
                  className={cn("me-2 size-4", value ? "opacity-0" : "opacity-100")}
                />
                <span className="text-muted-foreground">{emptyLabel}</span>
              </CommandItem>
              {options.map((option) => (
                <CommandItem
                  key={option}
                  value={option}
                  className="coarse:py-3"
                  onSelect={() => choose(option)}
                >
                  <Check
                    aria-hidden="true"
                    className={cn("me-2 size-4", value === option ? "opacity-100" : "opacity-0")}
                  />
                  <span className="truncate">{option}</span>
                </CommandItem>
              ))}
            </CommandGroup>

            {canCreate ? (
              <CommandGroup>
                <CommandItem
                  value={`__utiliser__${typed}`}
                  className="coarse:py-3"
                  onSelect={() => choose(typed)}
                >
                  <Plus aria-hidden="true" className="me-2 size-4" />
                  <span className="truncate">Utiliser « {typed} »</span>
                </CommandItem>
              </CommandGroup>
            ) : null}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}
