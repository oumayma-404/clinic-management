"use client";

import { useCallback, useEffect, useState } from "react";

import { Button } from "@/components/ui/button";
import { CardList } from "@/components/ui/card-list";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { PlatformNewsBroadcast } from "@/lib/api/platform";
import { formatDateTime } from "@/lib/format";
import { readJson, readRefusal } from "@/lib/refusal";

const POLL_MS = 5000;
const NETWORK_ERROR = "Impossible de joindre le serveur. Vérifiez que le tunnel est ouvert, puis réessayez.";

/**
 * Past news sends — a table above `lg:`, cards below. Re-read on its own (never the whole page), so a failed refresh
 * leaves the last list and the form above untouched.
 */
export function NewsHistory({
  initialItems,
  initialError,
  reloadKey,
}: {
  initialItems: PlatformNewsBroadcast[] | null;
  initialError: string | null;
  reloadKey: number;
}) {
  const [items, setItems] = useState(initialItems);
  const [error, setError] = useState(initialError);

  const load = useCallback(async () => {
    try {
      const response = await fetch("/bff/nouveautes/historique", { cache: "no-store" });
      if (!response.ok) {
        setError((await readRefusal(response)).error);
        return;
      }
      const data = await readJson<PlatformNewsBroadcast[]>(response);
      if (data === null) {
        setError("Réponse illisible du serveur.");
        return;
      }
      setItems(data);
      setError(null);
    } catch {
      setError(NETWORK_ERROR);
    }
  }, []);

  useEffect(() => {
    if (reloadKey > 0) {
      void load();
    }
  }, [reloadKey, load]);

  const markVisibility = useCallback((broadcastId: string, visibleInApp: boolean) => {
    setItems((current) =>
      current?.map((item) => (item.broadcastId === broadcastId ? { ...item, visibleInApp } : item)) ?? current,
    );
  }, []);

  const inProgress = items?.some((item) => item.inProgress) ?? false;
  useEffect(() => {
    if (!inProgress) {
      return;
    }
    const timer = window.setInterval(() => void load(), POLL_MS);
    return () => window.clearInterval(timer);
  }, [inProgress, load]);

  if (items === null) {
    return (
      <div className="space-y-3 rounded-lg border border-destructive/40 bg-card p-6" role="alert">
        <p className="text-sm text-destructive">L&apos;historique n&apos;a pas pu être lu : {error}</p>
        <Button type="button" variant="outline" onClick={() => void load()}>
          Réessayer
        </Button>
      </div>
    );
  }

  return (
    <>
      {error ? (
        <p className="text-sm text-muted-foreground" role="status">
          Actualisation impossible : {error}
        </p>
      ) : null}

      {items.length === 0 ? (
        <p className="rounded-lg border border-border bg-card p-6 text-sm text-muted-foreground" role="status">
          Aucun envoi pour l&apos;instant.
        </p>
      ) : (
        <>
          <div className="hidden lg:block">
            <div className="rounded-lg border border-border bg-card">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead scope="col">Envoyé le</TableHead>
                    <TableHead scope="col">Objet</TableHead>
                    <TableHead scope="col">Par</TableHead>
                    <TableHead scope="col">Cabinets</TableHead>
                    <TableHead scope="col">Résultat</TableHead>
                    <TableHead scope="col">Dans l&apos;app</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {items.map((item) => (
                    <TableRow key={item.broadcastId}>
                      <TableCell className="whitespace-nowrap">{formatDateTime(item.sentAt)}</TableCell>
                      <TableCell className="whitespace-normal">
                        <div className="max-w-80 [overflow-wrap:anywhere]">{item.subject}</div>
                      </TableCell>
                      <TableCell>{item.sentByName}</TableCell>
                      <TableCell>{item.clinics}</TableCell>
                      <TableCell>
                        <Outcome item={item} />
                      </TableCell>
                      <TableCell>
                        <AppVisibility item={item} onChanged={markVisibility} />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          </div>

          <div className="lg:hidden">
            <CardList
              items={items}
              getKey={(item) => item.broadcastId}
              title={(item) => <span className="whitespace-normal break-words">{item.subject}</span>}
              subtitle={(item) => `${formatDateTime(item.sentAt)} · ${item.sentByName}`}
              fields={(item) => [
                { label: "Cabinets", value: item.clinics },
                { label: "Résultat", value: <Outcome item={item} /> },
                { label: "Dans l'app", value: <AppVisibility item={item} onChanged={markVisibility} /> },
              ]}
            />
          </div>
        </>
      )}
    </>
  );
}

/** Whether the cabinets' « Nouveautés » page shows this news, and the one button that changes it. Reversible, so no confirm. */
function AppVisibility({
  item,
  onChanged,
}: {
  item: PlatformNewsBroadcast;
  onChanged: (broadcastId: string, visibleInApp: boolean) => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const action = item.visibleInApp ? "Masquer" : "Afficher";

  const toggle = async () => {
    setBusy(true);
    setError(null);
    try {
      const response = await fetch("/bff/nouveautes/visibilite", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ broadcastId: item.broadcastId, visible: !item.visibleInApp }),
      });
      if (!response.ok) {
        setError((await readRefusal(response)).error);
        return;
      }
      const data = await readJson<{ broadcastId: string; visibleInApp: boolean }>(response);
      onChanged(item.broadcastId, data?.visibleInApp ?? !item.visibleInApp);
    } catch {
      setError(NETWORK_ERROR);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5 whitespace-normal">
      <span className="inline-flex items-center gap-1.5 text-sm">
        <span
          aria-hidden="true"
          className={
            item.visibleInApp
              ? "size-2 rounded-full bg-primary"
              : "size-2 rounded-full border border-muted-foreground"
          }
        />
        {item.visibleInApp ? "Visible" : "Masquée"}
      </span>
      <Button
        type="button"
        size="sm"
        variant="outline"
        disabled={busy}
        onClick={() => void toggle()}
        aria-label={`${action} « ${item.subject} » dans l'app`}
      >
        {busy ? "…" : action}
      </Button>
      {error ? (
        <p className="w-full text-xs text-destructive" role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}

function Outcome({ item }: { item: PlatformNewsBroadcast }) {
  return (
    <div className="space-y-0.5 whitespace-normal">
      <p>
        {item.sent} / {item.recipients} envoyé{item.sent > 1 ? "s" : ""}
        {item.failed > 0 ? (
          <span className="text-destructive">
            {" "}
            · {item.failed} échec{item.failed > 1 ? "s" : ""}
          </span>
        ) : null}
        {item.inProgress ? <span className="text-muted-foreground"> · en cours…</span> : null}
      </p>
      {item.failedClinics.length > 0 ? (
        <p className="text-xs break-words text-muted-foreground">Non reçu : {item.failedClinics.join(", ")}</p>
      ) : null}
    </div>
  );
}
