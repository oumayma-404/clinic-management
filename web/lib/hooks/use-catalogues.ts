"use client"

import { useCallback, useMemo } from "react"
import { useQuery, useQueryClient, type UseQueryResult } from "@tanstack/react-query"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { medicationsApi } from "@/lib/api/medications"
import type { MedicationDto, ProcedureTypeDto } from "@/lib/api/types"
import { useSession } from "@/lib/auth/session"
import { queryKeys, realtimeMeta } from "@/lib/query/keys"

/** A shared catalogue read, in the shape its call sites already had (a list, a failure flag, a retry). */
export interface CatalogueRead<T> {
  /** Stable while the data is unchanged; empty until the first answer. */
  items: T[]
  /** No answer yet — neither a list nor a failure. */
  loading: boolean
  /** The read could not be made and there is no list to show: a failure, never an empty catalogue. */
  failed: boolean
  /** What failed, for a call site that words its own message (`getErrorMessage(error, fallback)`). */
  error: unknown
  retry: () => void
}

export interface CatalogueOptions {
  includeInactive?: boolean
  /** False until the screen needs the list (e.g. the editor reads médicaments only for an ordonnance). */
  enabled?: boolean
}

const NONE: never[] = []

function useCatalogueRead<T>(query: UseQueryResult<T[]>): CatalogueRead<T> {
  const { data, isPending, isError, error, refetch } = query
  return useMemo(
    () => ({
      items: data ?? NONE,
      loading: isPending && !isError,
      failed: isError && data === undefined,
      error,
      retry: () => void refetch(),
    }),
    [data, isPending, isError, error, refetch],
  )
}

/**
 * The clinic's act catalogue, read once per tab and shared — it was re-fetched by ten call sites, most on every
 * dialog open. Refreshed by the `proceduretypes` broadcast.
 */
export function useProcedureTypes({ includeInactive = false, enabled = true }: CatalogueOptions = {}): CatalogueRead<ProcedureTypeDto> {
  const { user } = useSession()
  const query = useQuery({
    queryKey: queryKeys.procedureTypes(includeInactive),
    queryFn: () => procedureTypesApi.list(includeInactive),
    enabled: enabled && Boolean(user),
    meta: realtimeMeta.procedureTypes,
  })
  return useCatalogueRead(query)
}

/** The whole medication catalogue (no search term), shared the same way. Refreshed by `medications`. */
export function useMedications({ includeInactive = false, enabled = true }: CatalogueOptions = {}): CatalogueRead<MedicationDto> {
  const { user } = useSession()
  const query = useQuery({
    queryKey: queryKeys.medications(includeInactive),
    queryFn: () => medicationsApi.list(undefined, includeInactive),
    enabled: enabled && Boolean(user),
    meta: realtimeMeta.medications,
  })
  return useCatalogueRead(query)
}

/** An act created from a picker (« Acte personnalisé ») joins every cached list at once; the broadcast then confirms it. */
export function useAddProcedureTypeToCache(): (created: ProcedureTypeDto) => void {
  const queryClient = useQueryClient()
  return useCallback(
    (created: ProcedureTypeDto) => {
      for (const includeInactive of [false, true]) {
        queryClient.setQueryData<ProcedureTypeDto[]>(queryKeys.procedureTypes(includeInactive), (prev) =>
          prev && !prev.some((p) => p.id === created.id) ? [...prev, created] : prev,
        )
      }
    },
    [queryClient],
  )
}
