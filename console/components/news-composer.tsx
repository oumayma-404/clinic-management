"use client";

import { ArrowDown, ArrowUp, ChevronDown, Plus, Trash2, X } from "lucide-react";
import { useEffect, useId, useRef, useState } from "react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet";
import type { PlatformNewsAudience, PlatformNewsItem, PlatformNewsQueued } from "@/lib/api/platform";
import { readJson, readRefusal } from "@/lib/refusal";

/** The palette offered per row; the first is the server's default emoji (check:responsive N10). */
const EMOJIS = ["✨", "🚀", "🦷", "📅", "🌴", "⏱️", "💰", "🔒", "📱", "🖨️", "📄", "💬", "📊", "🎉", "💡", "⚡", "🛠️", "🐛", "❤️"];

const DEFAULT_SUBJECT = "Les nouveautés Apexa ✨";
const MAX_ITEMS = 12;
const SUBJECT_MAX = 120;
const TEXT_MAX = 300;
/** A row's guide (2026-11-10): one paragraph and up to five numbered steps — the server's limits (N10). */
const DESCRIPTION_MAX = 400;
const MAX_STEPS = 5;
const STEP_MAX = 160;
const PREVIEW_DELAY_MS = 400;
const DRAFT_KEY = "apexa-console-news-draft";
const NETWORK_ERROR = "Impossible de joindre le serveur. Vérifiez que le tunnel est ouvert, puis réessayez.";
/** The preview is for reading: a tap on its button must not navigate the frame to a refused page. */
const NO_LINKS = "<style>a{pointer-events:none;cursor:default}</style>";

interface Row {
  key: string;
  emoji: string;
  text: string;
  description: string;
  steps: string[];
  /** The « Guide » disclosure — closed by default, so a row without one stays one line. */
  guideOpen: boolean;
}

const newRow = (): Row => ({
  key: crypto.randomUUID(),
  emoji: EMOJIS[0],
  text: "",
  description: "",
  steps: [],
  guideOpen: false,
});

const hasGuide = (row: Pick<Row, "description" | "steps">) =>
  row.description.trim().length > 0 || row.steps.some((step) => step.trim().length > 0);

type Notice = { tone: "ok" | "error"; text: string } | null;
type Outcome<T> = { ok: true; data: T } | { ok: false; error: string };
type AudienceCheck = "loading" | "fresh" | "failed";

async function call<T>(url: string, payload?: unknown): Promise<Outcome<T>> {
  try {
    const response = await fetch(url, payload === undefined
      ? { cache: "no-store" }
      : { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload) });
    if (!response.ok) {
      return { ok: false, error: (await readRefusal(response)).error };
    }
    const data = await readJson<T>(response);
    return data === null ? { ok: false, error: "Réponse illisible du serveur." } : { ok: true, data };
  } catch {
    return { ok: false, error: NETWORK_ERROR };
  }
}

/** One requestId per form, kept across a failed press, so a retry or a double-click sends once. */
export function NewsComposer({
  initialAudience,
  onSent,
}: {
  initialAudience: PlatformNewsAudience;
  onSent: () => void;
}) {
  const fieldId = useId();
  const subjectRef = useRef<HTMLInputElement>(null);
  const justSent = useRef(false);

  const [audience, setAudience] = useState(initialAudience);
  const [audienceCheck, setAudienceCheck] = useState<AudienceCheck>("loading");
  const [audienceError, setAudienceError] = useState<string | null>(null);

  const [requestId, setRequestId] = useState(() => crypto.randomUUID());
  const [subject, setSubject] = useState(DEFAULT_SUBJECT);
  const [rows, setRows] = useState<Row[]>(() => [newRow()]);
  const [restored, setRestored] = useState(false);
  const [focusTarget, setFocusTarget] = useState<string | null>(null);

  const [previewHtml, setPreviewHtml] = useState<string | null>(null);
  const [previewFailed, setPreviewFailed] = useState(false);
  const [previewNonce, setPreviewNonce] = useState(0);

  const [testing, setTesting] = useState(false);
  const [sending, setSending] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [notice, setNotice] = useState<Notice>(null);

  const items: PlatformNewsItem[] = rows.map(({ emoji, text, description, steps }) => ({ emoji, text, description, steps }));
  const hasContent = subject.trim().length > 0 && rows.some((r) => r.text.trim().length > 0);
  const canSend = hasContent && audience.recipients > 0;
  const body = JSON.stringify({ subject, items });
  const id = (part: string, key: string) => `${fieldId}-${part}-${key}`;

  useEffect(() => {
    try {
      const raw = sessionStorage.getItem(DRAFT_KEY);
      const draft = raw ? (JSON.parse(raw) as { subject?: unknown; items?: unknown }) : null;
      if (typeof draft?.subject === "string") {
        setSubject(draft.subject);
      }
      if (Array.isArray(draft?.items) && draft.items.length > 0) {
        setRows(
          (draft.items as Partial<PlatformNewsItem>[]).slice(0, MAX_ITEMS).map((r) => {
            const description = typeof r.description === "string" ? r.description : "";
            const steps = Array.isArray(r.steps)
              ? r.steps.filter((step): step is string => typeof step === "string").slice(0, MAX_STEPS)
              : [];
            return {
              key: crypto.randomUUID(),
              emoji: typeof r.emoji === "string" ? r.emoji : EMOJIS[0],
              text: typeof r.text === "string" ? r.text : "",
              description,
              steps,
              guideOpen: hasGuide({ description, steps }),
            };
          }),
        );
      }
    } catch {
      // No storage (private window): the draft simply is not kept.
    }
    setRestored(true);
  }, []);

  useEffect(() => {
    if (!restored) {
      return;
    }
    try {
      sessionStorage.setItem(DRAFT_KEY, body);
    } catch {
      // No storage: nothing to keep.
    }
  }, [restored, body]);

  useEffect(() => {
    if (focusTarget) {
      document.getElementById(focusTarget)?.focus();
      setFocusTarget(null);
    }
  }, [focusTarget]);

  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(async () => {
      try {
        const response = await fetch("/bff/nouveautes/apercu", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body,
          signal: controller.signal,
        });
        const data = response.ok ? await readJson<{ newsHtml: string }>(response) : null;
        if (controller.signal.aborted) {
          return;
        }
        setPreviewHtml(data?.newsHtml ?? null);
        setPreviewFailed(data === null);
      } catch {
        if (!controller.signal.aborted) {
          setPreviewHtml(null);
          setPreviewFailed(true);
        }
      }
    }, PREVIEW_DELAY_MS);

    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [body, previewNonce]);

  function updateRow(index: number, patch: Partial<Row>) {
    setRows((current) => current.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  }

  function addStep(index: number) {
    const row = rows[index];
    updateRow(index, { steps: [...row.steps, ""] });
    setFocusTarget(id(`step${row.steps.length}`, row.key));
  }

  function updateStep(index: number, stepIndex: number, value: string) {
    updateRow(index, { steps: rows[index].steps.map((step, i) => (i === stepIndex ? value : step)) });
  }

  function removeStep(index: number, stepIndex: number) {
    const row = rows[index];
    const steps = row.steps.filter((_, i) => i !== stepIndex);
    updateRow(index, { steps });
    setFocusTarget(steps.length === 0 ? id("add-step", row.key) : id(`step${Math.min(stepIndex, steps.length - 1)}`, row.key));
  }

  function addRow() {
    const row = newRow();
    setRows((current) => [...current, row]);
    setFocusTarget(id("text", row.key));
  }

  function moveRow(index: number, by: -1 | 1) {
    const moved = rows[index];
    const to = index + by;
    setRows((current) => {
      const next = [...current];
      next.splice(index, 1);
      next.splice(to, 0, moved);
      return next;
    });
    const atEdge = to === 0 || to === rows.length - 1;
    const sameDirection = by === -1 ? "up" : "down";
    const otherDirection = by === -1 ? "down" : "up";
    setFocusTarget(id(atEdge ? otherDirection : sameDirection, moved.key));
  }

  function deleteRow(index: number) {
    const next = rows.filter((_, i) => i !== index);
    setRows(next);
    setFocusTarget(id("text", next[Math.min(index, next.length - 1)].key));
  }

  async function refreshAudience() {
    setAudienceCheck("loading");
    const result = await call<PlatformNewsAudience>("/bff/nouveautes/audience");
    if (result.ok) {
      setAudience(result.data);
      setAudienceCheck("fresh");
    } else {
      setAudienceError(result.error);
      setAudienceCheck("failed");
    }
  }

  function confirmChanged(open: boolean) {
    if (open) {
      setConfirmOpen(true);
      void refreshAudience();
    } else if (!sending) {
      setConfirmOpen(false);
    }
  }

  async function sendTest() {
    setTesting(true);
    setNotice(null);
    const result = await call<{ testSentTo: string }>("/bff/nouveautes/test", { subject, items });
    setTesting(false);
    setNotice(
      result.ok
        ? { tone: "ok", text: `E-mail de test envoyé à ${result.data.testSentTo}.` }
        : { tone: "error", text: result.error },
    );
  }

  async function sendAll() {
    setSending(true);
    setNotice(null);
    const result = await call<PlatformNewsQueued>("/bff/nouveautes", { requestId, subject, items });
    setSending(false);
    setConfirmOpen(false);

    if (!result.ok) {
      setNotice({ tone: "error", text: result.error });
      return;
    }

    const queued = result.data;
    setNotice({
      tone: "ok",
      text: queued.alreadyQueued
        ? "Ces nouveautés étaient déjà envoyées : rien n'est reparti."
        : `Envoi en cours vers ${queued.recipients} adresse${queued.recipients > 1 ? "s" : ""}.`,
    });
    justSent.current = true;
    setRequestId(crypto.randomUUID());
    setSubject(DEFAULT_SUBJECT);
    setRows([newRow()]);
    onSent();
  }

  return (
    <div className="grid gap-6 lg:grid-cols-2">
      <section aria-labelledby={`${fieldId}-form`} className="min-w-0 space-y-4">
        <h2 id={`${fieldId}-form`} className="sr-only">
          Rédiger
        </h2>

        <div className="space-y-1.5">
          <Label htmlFor={`${fieldId}-subject`}>Objet</Label>
          <Input
            ref={subjectRef}
            id={`${fieldId}-subject`}
            value={subject}
            maxLength={SUBJECT_MAX}
            onChange={(e) => setSubject(e.target.value)}
            className="text-base"
          />
        </div>

        <fieldset className="space-y-2">
          <legend className="text-sm font-medium">Nouveautés</legend>
          <ol className="space-y-2">
            {rows.map((row, index) => (
              <li key={row.key} className="space-y-2 rounded-lg border border-border bg-card p-3">
                <div className="flex flex-col gap-2 sm:flex-row sm:items-start">
                  <div className="flex min-w-0 flex-1 items-start gap-2">
                    <select
                      aria-label={`Emoji de la nouveauté ${index + 1}`}
                      value={row.emoji}
                      onChange={(e) => updateRow(index, { emoji: e.target.value })}
                      className="h-11 w-16 shrink-0 rounded-md border border-input bg-transparent text-center text-xl"
                    >
                      {EMOJIS.map((emoji) => (
                        <option key={emoji} value={emoji}>
                          {emoji}
                        </option>
                      ))}
                    </select>
                    <textarea
                      id={id("text", row.key)}
                      aria-label={`Nouveauté ${index + 1}`}
                      rows={3}
                      maxLength={TEXT_MAX}
                      value={row.text}
                      onChange={(e) => updateRow(index, { text: e.target.value })}
                      placeholder="Ex. : Les rappels SMS partent maintenant la veille à 18 h."
                      className="min-h-11 w-full min-w-0 flex-1 rounded-md border border-input bg-transparent px-3 py-2 text-base"
                    />
                  </div>
                  <div className="flex shrink-0 justify-end gap-2">
                    <Button
                      id={id("up", row.key)}
                      type="button"
                      variant="ghost"
                      size="icon"
                      className="coarse:size-11"
                      aria-label={`Monter la nouveauté ${index + 1}`}
                      disabled={index === 0}
                      onClick={() => moveRow(index, -1)}
                    >
                      <ArrowUp />
                    </Button>
                    <Button
                      id={id("down", row.key)}
                      type="button"
                      variant="ghost"
                      size="icon"
                      className="coarse:size-11"
                      aria-label={`Descendre la nouveauté ${index + 1}`}
                      disabled={index === rows.length - 1}
                      onClick={() => moveRow(index, 1)}
                    >
                      <ArrowDown />
                    </Button>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      className="ms-2 coarse:size-11"
                      aria-label={`Supprimer la nouveauté ${index + 1}`}
                      disabled={rows.length === 1}
                      onClick={() => deleteRow(index)}
                    >
                      <Trash2 />
                    </Button>
                  </div>
                </div>
                <NewsRowGuide
                  row={row}
                  position={index + 1}
                  id={(part) => id(part, row.key)}
                  onToggle={() => updateRow(index, { guideOpen: !row.guideOpen })}
                  onDescription={(description) => updateRow(index, { description })}
                  onStep={(stepIndex, value) => updateStep(index, stepIndex, value)}
                  onAddStep={() => addStep(index)}
                  onRemoveStep={(stepIndex) => removeStep(index, stepIndex)}
                />
              </li>
            ))}
          </ol>
          <Button type="button" variant="outline" disabled={rows.length >= MAX_ITEMS} onClick={addRow}>
            <Plus />
            Ajouter une nouveauté
          </Button>
        </fieldset>

        {notice ? (
          <p
            role={notice.tone === "error" ? "alert" : "status"}
            className={
              notice.tone === "error"
                ? "rounded-md border border-destructive/40 p-3 text-sm text-destructive"
                : "rounded-md border border-border bg-muted/40 p-3 text-sm"
            }
          >
            {notice.text}
          </p>
        ) : null}

        <div className="flex flex-col gap-2 border-t border-border pt-4 sm:flex-row sm:flex-wrap">
          <Button type="button" variant="outline" disabled={!hasContent || testing} onClick={sendTest}>
            {testing ? "Envoi du test…" : "M'envoyer un test"}
          </Button>
          <Sheet open={confirmOpen} onOpenChange={confirmChanged}>
            <SheetTrigger asChild>
              <Button type="button" disabled={!canSend || sending}>
                Envoyer à {audience.clinics} cabinet{audience.clinics > 1 ? "s" : ""}
              </Button>
            </SheetTrigger>
            <SheetContent
              side="bottom"
              onCloseAutoFocus={(e) => {
                if (justSent.current) {
                  e.preventDefault();
                  justSent.current = false;
                  subjectRef.current?.focus();
                }
              }}
              className="max-h-[85dvh] overflow-hidden p-0 lg:inset-auto lg:top-1/2 lg:left-1/2 lg:max-h-[85dvh] lg:w-[calc(100%-2rem)] lg:max-w-lg lg:-translate-x-1/2 lg:-translate-y-1/2 lg:rounded-xl lg:border"
            >
              <div className="flex max-h-[85dvh] min-h-0 flex-col">
                <SheetHeader className="shrink-0 border-b border-border">
                  <SheetTitle>Envoyer les nouveautés ?</SheetTitle>
                  <SheetDescription className="break-words">{subject}</SheetDescription>
                </SheetHeader>
                <div className="min-h-0 flex-1 space-y-2 overflow-y-auto p-4 text-sm" role="status">
                  {audienceCheck === "loading" ? (
                    <p className="text-muted-foreground">Vérification des destinataires…</p>
                  ) : audienceCheck === "failed" ? (
                    <p className="text-destructive">{audienceError}</p>
                  ) : (
                    <>
                      <p>
                        <strong>{audience.clinics}</strong> cabinet{audience.clinics > 1 ? "s" : ""} ·{" "}
                        <strong>{audience.recipients}</strong> adresse{audience.recipients > 1 ? "s" : ""}{" "}
                        d&apos;administrateur
                      </p>
                      {audience.clinicsWithoutAdminEmail > 0 ? (
                        <p className="text-muted-foreground">
                          {audience.clinicsWithoutAdminEmail} cabinet{audience.clinicsWithoutAdminEmail > 1 ? "s" : ""}{" "}
                          sans adresse d&apos;administrateur ne le recevr
                          {audience.clinicsWithoutAdminEmail > 1 ? "ont" : "a"} pas.
                        </p>
                      ) : null}
                    </>
                  )}
                </div>
                <div className="flex shrink-0 flex-col gap-2 border-t border-border p-4 sm:flex-row-reverse">
                  <Button
                    type="button"
                    disabled={sending || audienceCheck !== "fresh" || audience.recipients === 0}
                    onClick={sendAll}
                  >
                    {sending ? "Envoi…" : "Envoyer"}
                  </Button>
                  <Button type="button" variant="outline" disabled={sending} onClick={() => confirmChanged(false)}>
                    Revenir
                  </Button>
                </div>
              </div>
            </SheetContent>
          </Sheet>
        </div>

        <section aria-labelledby={`${fieldId}-recipients`} className="min-w-0 space-y-2">
          <h2 id={`${fieldId}-recipients`} className="text-sm font-medium">
            Destinataires{" "}
            <span className="font-normal text-muted-foreground">
              · {audience.recipients} adresse{audience.recipients > 1 ? "s" : ""} · {audience.clinics} cabinet
              {audience.clinics > 1 ? "s" : ""}
            </span>
          </h2>
          {audience.recipients === 0 ? (
            <p className="text-sm text-muted-foreground" role="status">
              Aucun cabinet actif ou en essai n&apos;a d&apos;administrateur avec une adresse e-mail.
            </p>
          ) : (
            // Scrolls on its own only beside the sticky preview; on a phone the page scrolls. `relative` clips its rows.
            <ul className="relative divide-y divide-border rounded-lg border border-border bg-card lg:max-h-[min(28rem,60dvh)] lg:overflow-y-auto">
              {audience.recipientList.map((recipient) => (
                <li key={recipient.adminEmail} className="min-w-0 px-3 py-2 text-sm">
                  <p className="font-medium [overflow-wrap:anywhere]">{recipient.clinicName}</p>
                  <p className="text-muted-foreground [overflow-wrap:anywhere]">
                    {recipient.adminName ? `${recipient.adminName} · ` : ""}
                    {recipient.adminEmail}
                  </p>
                </li>
              ))}
            </ul>
          )}
          {audience.skippedClinics.length > 0 ? (
            <p className="text-sm text-muted-foreground [overflow-wrap:anywhere]">
              Sans adresse d&apos;administrateur, ne reçoi{audience.skippedClinics.length > 1 ? "vent" : "t"} rien :{" "}
              {audience.skippedClinics.join(", ")}.
            </p>
          ) : null}
        </section>
      </section>

      <section aria-labelledby={`${fieldId}-preview`} className="min-w-0 space-y-2 lg:sticky lg:top-4 lg:self-start">
        <h2 id={`${fieldId}-preview`} className="text-sm font-medium">
          Aperçu
        </h2>
        {previewFailed ? (
          <div
            role="status"
            className="flex h-[min(36rem,70dvh)] flex-col items-center justify-center gap-3 rounded-lg border border-destructive/40 p-4 text-center"
          >
            <p className="text-sm text-destructive">L&apos;aperçu n&apos;a pas pu être affiché.</p>
            <Button type="button" variant="outline" onClick={() => setPreviewNonce((n) => n + 1)}>
              Réessayer
            </Button>
          </div>
        ) : previewHtml ? (
          <iframe
            title="Aperçu de l'e-mail"
            srcDoc={previewHtml.replace("</head>", `${NO_LINKS}</head>`)}
            sandbox=""
            className="h-[min(36rem,70dvh)] w-full rounded-lg border border-border bg-white"
          />
        ) : (
          <div
            className="h-[min(36rem,70dvh)] w-full animate-pulse rounded-lg border border-border bg-muted/40"
            aria-hidden
          />
        )}
      </section>
    </div>
  );
}

/**
 * A row's optional guide (owner's request, 2026-11-10): a description and up to five numbered steps, rendered under
 * the row in the e-mail and on the app's « Nouveautés » page as « Comment ça marche ». Collapsed by default so a row
 * without one stays one line; it opens by itself when a restored draft carries one.
 */
function NewsRowGuide({
  row,
  position,
  id,
  onToggle,
  onDescription,
  onStep,
  onAddStep,
  onRemoveStep,
}: {
  row: Row;
  position: number;
  id: (part: string) => string;
  onToggle: () => void;
  onDescription: (description: string) => void;
  onStep: (stepIndex: number, value: string) => void;
  onAddStep: () => void;
  onRemoveStep: (stepIndex: number) => void;
}) {
  const filled = row.steps.filter((step) => step.trim().length > 0).length;
  const label = row.guideOpen ? "Masquer le guide" : hasGuide(row) ? "Modifier le guide" : "Ajouter un guide";

  return (
    <div className="min-w-0">
      <Button
        type="button"
        variant="ghost"
        size="sm"
        className="-ms-2 coarse:h-11"
        aria-expanded={row.guideOpen}
        aria-controls={row.guideOpen ? id("guide") : undefined}
        aria-label={`${label} de la nouveauté ${position}`}
        onClick={onToggle}
      >
        <ChevronDown className={row.guideOpen ? "rotate-180" : undefined} />
        {label}
        {!row.guideOpen && filled > 0 ? (
          <span className="font-normal text-muted-foreground">
            · {filled} étape{filled > 1 ? "s" : ""}
          </span>
        ) : null}
      </Button>

      {row.guideOpen ? (
        <div id={id("guide")} className="mt-2 space-y-3 rounded-md border border-dashed border-border p-3">
          <div className="space-y-1.5">
            <Label htmlFor={id("desc")}>Description</Label>
            <textarea
              id={id("desc")}
              rows={2}
              maxLength={DESCRIPTION_MAX}
              value={row.description}
              onChange={(e) => onDescription(e.target.value)}
              placeholder="Ex. : La secrétaire demande, vous décidez."
              className="min-h-11 w-full min-w-0 rounded-md border border-input bg-transparent px-3 py-2 text-base"
            />
          </div>

          <fieldset className="min-w-0 space-y-2">
            <legend className="text-sm font-medium">Comment ça marche</legend>
            {row.steps.length > 0 ? (
              <ol className="space-y-2">
                {row.steps.map((step, stepIndex) => (
                  <li key={stepIndex} className="flex min-w-0 items-center gap-2">
                    <span aria-hidden className="w-5 shrink-0 text-end text-sm font-semibold text-muted-foreground">
                      {stepIndex + 1}.
                    </span>
                    <Input
                      id={id(`step${stepIndex}`)}
                      aria-label={`Étape ${stepIndex + 1} du guide de la nouveauté ${position}`}
                      value={step}
                      maxLength={STEP_MAX}
                      onChange={(e) => onStep(stepIndex, e.target.value)}
                      placeholder={stepIndex === 0 ? "Ex. : Paramètres → Équipe : activez « Congés »" : undefined}
                      className="min-w-0 flex-1 text-base"
                    />
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      className="coarse:size-11"
                      aria-label={`Supprimer l'étape ${stepIndex + 1} de la nouveauté ${position}`}
                      onClick={() => onRemoveStep(stepIndex)}
                    >
                      <X />
                    </Button>
                  </li>
                ))}
              </ol>
            ) : null}
            <Button
              id={id("add-step")}
              type="button"
              variant="outline"
              size="sm"
              className="coarse:h-11"
              disabled={row.steps.length >= MAX_STEPS}
              onClick={onAddStep}
            >
              <Plus />
              Ajouter une étape
            </Button>
          </fieldset>
        </div>
      ) : null}
    </div>
  );
}
