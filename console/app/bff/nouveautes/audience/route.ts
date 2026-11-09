import { forwardGet } from "@/lib/bff";

/** Who « Envoyer » reaches right now — re-read when the confirm opens. */
export async function GET() {
  return forwardGet("/platform/news/audience");
}
