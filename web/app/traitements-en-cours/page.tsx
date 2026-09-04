import { redirect } from "next/navigation"

/**
 * « Traitements en cours » no longer has a page of its own — it is the **lead section** of
 * `/treatment-plans`, which is now the single treatments screen.
 *
 * <p>Two screens over one subject was the problem: the worklist answered « qu'est-ce qui reste ? » and the
 * devis list answered « qu'a-t-on convenu ? » about the same acts, one rail entry apart, and neither linked to
 * the other. They are one page now, the worklist first.</p>
 *
 * <p>⚠️ <b>The route is kept as a redirect, not deleted.</b> It shipped in the rail, in the journée's
 * « traitements en cours » pastille and in `lib/dashboard-links.ts`, so it is in browser histories and possibly
 * bookmarked. A 404 on a URL the product itself emitted last week is indistinguishable, to the person meeting
 * it, from the feature having been withdrawn.</p>
 */
export default function TreatmentsInProgressRedirect() {
  redirect("/treatment-plans")
}
