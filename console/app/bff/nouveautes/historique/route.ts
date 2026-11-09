import { forwardGet } from "@/lib/bff";

/** The history of sends, re-read by the screen while a send is in progress. */
export async function GET() {
  return forwardGet("/platform/news");
}
