/**
 * Set when a demo visitor enters, and outlives the session (which ends with its cookie): it is what tells a visitor
 * whose session ran out from a first-time one, since both reach the app with no session at all. Not a credential.
 * Its own module because the BFF route (server) and the demo provider (client) both name it.
 */
export const DEMO_VISIT_COOKIE = "demo_visit"
