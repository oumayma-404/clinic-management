package com.clinicmanagement.shell

import android.content.Intent

/**
 * What a tapped phone notification asks to open (staff-chat Part 7). FCM puts the server's `data` block into the
 * launch intent's extras; both values come from outside the app, so they are checked before they reach a page.
 */
data class PushTap(val conversation: String?, val appointmentId: String?) {
    companion object {
        const val CONVERSATION = "conversation"
        const val APPOINTMENT = "appointmentId"

        private val GUID = Regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")
        private const val MAX_CONVERSATION = 600

        fun from(intent: Intent?): PushTap? {
            val extras = intent?.extras ?: return null
            val conversation = extras.getString(CONVERSATION)?.takeIf { it.isNotBlank() && it.length <= MAX_CONVERSATION }
            val appointment = extras.getString(APPOINTMENT)?.takeIf { GUID.matches(it) }
            if (conversation == null && appointment == null) return null
            return PushTap(conversation, appointment)
        }
    }
}
