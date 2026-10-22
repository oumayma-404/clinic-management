package com.clinicmanagement.shell

/**
 * `clinic-pc-copy` D23 — the one form state the app holds across a switch of server (AC-3.2). The page hands it over
 * as it is typed (`carryDraft`) and the next clinic page takes it once (`takeCarriedDraft`). The Windows shell's
 * `CarriedDraftSlot.cs`, line for line.
 *
 * ⚠️ Memory only, never disk: a fiche is a patient's clinical record.
 */
object CarriedDraftSlot {
    /** The page caps a draft at this size too; anything larger is not a form somebody typed. */
    const val MAX_CHARS = 2_000_000

    /** Older than this, it is not the form somebody was in the middle of. */
    const val MAX_AGE_MS = 2 * 60 * 60 * 1000L

    private var draft: String? = null
    private var at = 0L

    /** Replaces what is held; empty forgets it, and so does an oversized one. */
    @Synchronized
    fun carry(value: String?, nowMs: Long) {
        if (value.isNullOrEmpty() || value.length > MAX_CHARS) {
            draft = null
            return
        }
        draft = value
        at = nowMs
    }

    /** Hands the draft over once and forgets it; null when there is none or it is too old. */
    @Synchronized
    fun take(nowMs: Long): String? {
        val held = draft
        draft = null
        return if (held != null && nowMs - at <= MAX_AGE_MS) held else null
    }
}
