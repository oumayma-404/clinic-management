import { forwardPost } from "@/lib/bff";

/** « Envoyer » — records the broadcast and queues the e-mails. */
export async function POST(request: Request) {
  return forwardPost(request, "/platform/news");
}
