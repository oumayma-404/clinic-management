"use client"

import { RappelsScreen } from "./rappels-screen"

/**
 * « Rappels » — the delivery log. It was parked behind « Bientôt disponible » until an SMS sender was contracted;
 * since sms-rappels-tunisie-sms the SMS reminders go out through Tunisie SMS, so the log is real again.
 */
export default function RappelsPage() {
  return <RappelsScreen />
}
