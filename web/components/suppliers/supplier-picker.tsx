"use client"

import { useEffect, useMemo, useState } from "react"
import { Check, ChevronsUpDown, Plus } from "lucide-react"
import { Button } from "@/components/ui/button"
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui/command"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { suppliersApi } from "@/lib/api/suppliers"
import type { SupplierDto } from "@/lib/api/types"
import { cn } from "@/lib/utils"

/**
 * « Fournisseur » — a searchable picker over the clinic's contacts, shared by the stock item form and the bon de
 * prothèse form.
 *
 * <p>It replaced a free-text `<Input>` on each, which is the whole feature in miniature: two forms each let a
 * user type a name, so the same dépôt existed under three spellings and none of them had a number behind it.</p>
 *
 * <p><b>« Aucun » is a real option, listed first.</b> Most stock articles have no supplier (AC-5), and clearing
 * a link has to be as reachable as setting one — a picker you can only ever add to is how a mis-assigned
 * supplier becomes permanent.</p>
 *
 * <p>⚠️ <b>A picked supplier that is no longer in the fetched list is still rendered</b>, from `selectedFallback`.
 * The list is active-only, so without that a deactivated supplier would silently read as « Aucun » on the form
 * of an article that still links to it — and the next save would clear a link the user never touched (EC-4).</p>
 */
interface SupplierPickerProps {
  value: string | null
  onChange: (supplierId: string | null) => void
  /**
   * The supplier this record already names, when it is known — used to keep a deactivated one visible. Pass the
   * `supplierId`/`supplierName` the row was read with.
   */
  selectedFallback?: { id: string; name: string } | null
  /** Opens the inline « + Créer un fournisseur » flow. Omit to hide that row. */
  onCreateNew?: () => void
  /** Bumped by the parent after a create, so a freshly-made supplier appears without a remount. */
  reloadKey?: number
  id?: string
  disabled?: boolean
}

export function SupplierPicker({
  value,
  onChange,
  selectedFallback,
  onCreateNew,
  reloadKey = 0,
  id,
  disabled,
}: SupplierPickerProps) {
  const [open, setOpen] = useState(false)
  const [suppliers, setSuppliers] = useState<SupplierDto[]>([])
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setFailed(false)
    suppliersApi
      .list()
      .then((rows) => {
        if (!cancelled) setSuppliers(rows)
      })
      // A failed read must not render as « aucun fournisseur » — that reads as "this clinic has none", which is
      // a claim about the data where the truth is a claim about the network.
      .catch(() => {
        if (!cancelled) setFailed(true)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [reloadKey])

  const options = useMemo(() => {
    if (!selectedFallback || suppliers.some((s) => s.id === selectedFallback.id)) return suppliers
    // Deactivated (or simply absent from the active list) but still linked — keep it pickable so re-saving the
    // record does not silently drop the link.
    return [
      ...suppliers,
      {
        id: selectedFallback.id,
        name: selectedFallback.name,
        isActive: false,
        linkedItemCount: 0,
        linkedLabOrderCount: 0,
        version: 0,
        createdAt: "",
      } as SupplierDto,
    ]
  }, [suppliers, selectedFallback])

  const selected = options.find((s) => s.id === value) ?? null

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
          <span className={cn("truncate", !selected && "text-muted-foreground")}>
            {selected ? selected.name : "Aucun fournisseur"}
          </span>
          <ChevronsUpDown aria-hidden="true" className="ms-2 size-4 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      {/* Never a bare `w-80`: at 320 px that is the whole viewport with no gutter left. */}
      <PopoverContent className="w-[min(22rem,calc(100vw-2rem))] p-0" align="start">
        <Command>
          <CommandInput placeholder="Rechercher un fournisseur…" />
          <CommandList>
            <CommandEmpty>
              {loading
                ? "Chargement…"
                : failed
                  ? "Les fournisseurs n'ont pas pu être chargés."
                  : "Aucun fournisseur trouvé."}
            </CommandEmpty>
            <CommandGroup>
              <CommandItem
                value="aucun-fournisseur"
                className="coarse:py-3"
                onSelect={() => {
                  onChange(null)
                  setOpen(false)
                }}
              >
                <Check
                  aria-hidden="true"
                  className={cn("me-2 size-4", value === null ? "opacity-100" : "opacity-0")}
                />
                <span className="text-muted-foreground">Aucun</span>
              </CommandItem>

              {options.map((supplier) => (
                <CommandItem
                  key={supplier.id}
                  // cmdk matches on `value`, so the catégorie is joined in — otherwise « prothèse » would find
                  // the group heading by eye and nothing by keyboard.
                  value={`${supplier.name} ${supplier.category ?? ""}`}
                  className="coarse:py-3"
                  onSelect={() => {
                    onChange(supplier.id)
                    setOpen(false)
                  }}
                >
                  <Check
                    aria-hidden="true"
                    className={cn("me-2 size-4", value === supplier.id ? "opacity-100" : "opacity-0")}
                  />
                  <span className="min-w-0 flex-1 truncate">{supplier.name}</span>
                  {supplier.category ? (
                    <span className="ms-2 shrink-0 text-2xs text-muted-foreground">{supplier.category}</span>
                  ) : null}
                  {!supplier.isActive ? (
                    <span className="ms-2 shrink-0 text-2xs text-muted-foreground">désactivé</span>
                  ) : null}
                </CommandItem>
              ))}
            </CommandGroup>

            {onCreateNew ? (
              // Its own group, so creating a fournisseur is not filed under whichever contact happens to sort last.
              <CommandGroup>
                <CommandItem
                  value="creer-un-fournisseur"
                  className="coarse:py-3"
                  onSelect={() => {
                    setOpen(false)
                    onCreateNew()
                  }}
                >
                  <Plus aria-hidden="true" className="me-2 size-4" />
                  Créer un fournisseur
                </CommandItem>
              </CommandGroup>
            ) : null}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}
