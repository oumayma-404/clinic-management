"use client";

import { useState } from "react";

import { NewsComposer } from "@/components/news-composer";
import { NewsHistory } from "@/components/news-history";
import type { PlatformNewsAudience, PlatformNewsBroadcast } from "@/lib/api/platform";

/** The form and the history side by side; a send re-reads only the history, never the page. */
export function NewsWorkspace({
  audience,
  history,
  historyError,
}: {
  audience: PlatformNewsAudience;
  history: PlatformNewsBroadcast[] | null;
  historyError: string | null;
}) {
  const [reloadKey, setReloadKey] = useState(0);

  return (
    <>
      <NewsComposer initialAudience={audience} onSent={() => setReloadKey((k) => k + 1)} />

      <section className="mt-10 space-y-3" aria-labelledby="news-history-title">
        <h2 id="news-history-title" className="text-lg font-semibold">
          Envois précédents
        </h2>
        <NewsHistory initialItems={history} initialError={historyError} reloadKey={reloadKey} />
      </section>
    </>
  );
}
