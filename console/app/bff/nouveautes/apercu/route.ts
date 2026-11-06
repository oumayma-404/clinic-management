import { forwardPost } from "@/lib/bff";

/** The live preview, rendered by the server's own e-mail layout. Writes nothing. */
export async function POST(request: Request) {
  return forwardPost(request, "/platform/news/preview");
}
