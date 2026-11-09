import { forwardPost } from "@/lib/bff";

/** « M'envoyer un test » — to the signed-in console account only. */
export async function POST(request: Request) {
  return forwardPost(request, "/platform/news/test");
}
