import { apiGet } from "./client"

/**
 * « Journal d'activité » — the read side of the audit ledger (AC-19).
 *
 * The endpoint (`GET /api/audit`, `AdminOnly`) shipped with the ledger and had **no client at all**, so every
 * mutation in the product was being recorded into a table nothing could open. Nothing here changes the endpoint;
 * this module is the missing half.
 */

/** One mutated aggregate: who, what, which action, when. */
export interface AuditEntryDto {
  id: string
  /** The actor's id — a `User.Id`, or `job|<name>` for a background job or console verb. */
  userId: string
  userEmail?: string | null
  /** Who to show: the email, else « Tâche automatique (…) », else the raw id. Never « — ». */
  actorLabel: string
  /** True when the actor is a process rather than a person — mark the row without parsing `userId`. */
  isSystemActor: boolean
  /** CLR aggregate name (`Patient`, `Invoice`) — the stable filter key. */
  entityType: string
  /** « Patient », « Note d'honoraires » — the French name for the same thing. */
  entityLabel: string
  entityId: string
  /** `Insert` | `Update` | `Delete`. */
  action: string
  /** « Création » | « Modification » | « Suppression ». */
  actionLabel: string
  /** Compact summary of what moved. Null for a creation. */
  changedFields?: string | null
  occurredAt: string
}

/** A « Type » filter option: stable key + French label. */
export interface AuditEntityTypeOptionDto {
  value: string
  label: string
}

/**
 * An « Auteur » filter option: the stored actor id + the name to show for it.
 *
 * ⚠️ Derived server-side from the ledger, not from the `Users` table — a colleague who has left still appears in
 * the history, and « qu'a fait cette personne ? » is asked about them most of all.
 */
export interface AuditActorOptionDto {
  value: string
  label: string
}

/**
 * One page of the ledger.
 *
 * ⚠️ `entityTypes` is **derived from the rows this clinic actually has** and travels with the page rather than
 * being fetched separately — the filter is useless without it, and a second round trip is a second thing that can
 * fail. Never build that list client-side from `items`: over a page it would offer only the types on screen.
 */
export interface AuditPageDto {
  items: AuditEntryDto[]
  entityTypes: AuditEntityTypeOptionDto[]
  actors: AuditActorOptionDto[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
}

export interface AuditQuery {
  entityType?: string
  entityId?: string
  /** Inclusive calendar day `yyyy-MM-dd`, read in the **clinic's** zone by the server. */
  from?: string
  to?: string
  /** `Insert` | `Update` | `Delete`. An unrecognised value is ignored server-side, never refused. */
  action?: string
  /** One actor's entries — the id from `AuditPageDto.actors`, never a typed name. */
  userId?: string
  page?: number
  pageSize?: number
}

export const auditApi = {
  /**
   * ⚠️ Omitting the paging parameters gets the **first page**, not everything — deliberately the opposite of the
   * list reads, because this table grows with every save the practice has ever made.
   */
  list: async (query: AuditQuery = {}): Promise<AuditPageDto> => {
    const params = new URLSearchParams()
    if (query.entityType) params.set("entityType", query.entityType)
    if (query.entityId) params.set("entityId", query.entityId)
    if (query.from) params.set("from", query.from)
    if (query.to) params.set("to", query.to)
    if (query.action) params.set("action", query.action)
    if (query.userId) params.set("userId", query.userId)
    if (query.page != null) params.set("page", String(query.page))
    if (query.pageSize != null) params.set("pageSize", String(query.pageSize))

    const qs = params.toString()
    return apiGet<AuditPageDto>(`/audit${qs ? `?${qs}` : ""}`)
  },
}
