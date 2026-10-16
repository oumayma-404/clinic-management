"use client"

import { useEffect, useLayoutEffect, useRef, useState } from "react"
import { Ban, MessageCircle } from "lucide-react"
import { Button } from "@/components/ui/button"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { staffChatApi, type StaffConversationDto, type StaffQuickMessageDto } from "@/lib/api/staff-chat"
import { showErrorToast } from "@/lib/errors"
import { useMarkThreadRead, useStaffChatThread, useStaffQuickMessages } from "@/lib/hooks/use-staff-chat"
import { claimViewFor, seenLine } from "@/lib/staff-chat/desk"
import { MessageBubble } from "./message-bubble"
import { MessageComposer } from "./message-composer"
import { QuickMessageBar } from "./quick-message-bar"
import { QuickMessagesEditorDialog } from "./quick-messages-editor-dialog"
import { PatientLinkPicker, type PickedLink } from "./patient-link-picker"

interface MessageThreadProps {
  conversationKey: string
  /** Null until the list has loaded, or when the key no longer names a conversation offered to this person. */
  conversation: StaffConversationDto | null
  viewer: { userId: string; name: string } | null
}

/** How close to the bottom still counts as « reading the latest », so a new message scrolls into view. */
const NEAR_BOTTOM_PX = 120

export function MessageThread({ conversationKey, conversation, viewer }: MessageThreadProps) {
  const thread = useStaffChatThread(conversationKey, viewer)
  useMarkThreadRead(conversationKey, thread.messages, viewer?.userId)
  const quickMessages = useStaffQuickMessages(true)
  const [editing, setEditing] = useState(false)
  const [claiming, setClaiming] = useState<string | null>(null)
  const [attachment, setAttachment] = useState<PickedLink | null>(null)
  const [picker, setPicker] = useState<{ quick: StaffQuickMessageDto | null } | null>(null)
  const quickPending = picker?.quick ?? null

  const sendQuickWith = (item: StaffQuickMessageDto, link: PickedLink | null) =>
    thread.send({
      body: null,
      quickMessage: { key: item.key, label: item.label, urgent: item.urgent },
      patient: link?.patient ?? null,
      appointment: link?.appointment ?? null,
    })

  // A quick message about a patient offers the picker, and may still go without one (AC-2.5).
  const sendQuick = (item: StaffQuickMessageDto) => {
    if (!item.asksForPatient) {
      sendQuickWith(item, null)
    } else if (attachment) {
      sendQuickWith(item, attachment)
      setAttachment(null)
    } else {
      setPicker({ quick: item })
    }
  }

  const scrollerRef = useRef<HTMLDivElement>(null)
  const pinnedToBottom = useRef(true)
  const lastCount = useRef(0)
  const firstOlderId = useRef<string | null>(null)
  const heightBeforeOlder = useRef<number | null>(null)

  // New messages follow the reader down only if they were already at the bottom, or wrote the message themselves.
  useLayoutEffect(() => {
    const el = scrollerRef.current
    if (!el) return
    const messages = thread.messages
    const first = messages[0]?.id ?? null

    if (heightBeforeOlder.current !== null && first !== firstOlderId.current) {
      el.scrollTop += el.scrollHeight - heightBeforeOlder.current
      heightBeforeOlder.current = null
    } else if (messages.length > lastCount.current) {
      const newest = messages[messages.length - 1]
      const mineJustSent = newest?.localId !== undefined
      if (pinnedToBottom.current || mineJustSent || lastCount.current === 0) el.scrollTop = el.scrollHeight
    }
    firstOlderId.current = first
    lastCount.current = messages.length
  }, [thread.messages])

  useEffect(() => {
    lastCount.current = 0
    pinnedToBottom.current = true
  }, [conversationKey])

  const loadOlder = () => {
    const el = scrollerRef.current
    if (el) heightBeforeOlder.current = el.scrollHeight
    void thread.loadOlder()
  }

  // « Je m'en occupe » / « Je ne peux plus »: the answer replaces the message; a refusal names who has it (AC-5.2).
  const changeClaim = async (messageId: string, take: boolean) => {
    setClaiming(messageId)
    try {
      thread.apply(await (take ? staffChatApi.claim(messageId) : staffChatApi.releaseClaim(messageId)))
    } catch (err) {
      showErrorToast(err)
      void thread.reload()
    } finally {
      setClaiming(null)
    }
  }

  const lastSent = [...thread.messages].reverse().find((m) => !m.localId)
  const shared = conversation ? conversation.kind !== "direct" : false
  const readOnly = conversation?.readOnly ?? false

  return (
    <>
      <div
        ref={scrollerRef}
        onScroll={(event) => {
          const el = event.currentTarget
          pinnedToBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < NEAR_BOTTOM_PX
        }}
        className="relative min-h-0 flex-1 overflow-y-auto px-3 py-3"
      >
        {thread.error && thread.messages.length === 0 ? (
          <LoadFailureNotice message="Les messages n'ont pas pu être chargés." onRetry={() => void thread.reload()} />
        ) : thread.loading && thread.messages.length === 0 ? (
          <div className="space-y-3" aria-busy="true" aria-label="Chargement des messages">
            {[0, 1, 2].map((i) => (
              <div key={i} className={i % 2 ? "flex justify-end" : "flex"}>
                <div className="h-10 w-2/3 animate-pulse rounded-2xl bg-muted" />
              </div>
            ))}
          </div>
        ) : thread.messages.length === 0 ? (
          <EmptyState size="compact" icon={MessageCircle} title="Aucun message" />
        ) : (
          <>
            {thread.hasOlder && (
              <div className="mb-3 flex justify-center">
                <Button variant="ghost" size="sm" onClick={loadOlder} disabled={thread.loadingOlder} className="coarse:min-h-11">
                  {thread.loadingOlder ? "Chargement…" : "Messages plus anciens"}
                </Button>
              </div>
            )}
            {thread.error && <LoadFailureNotice variant="inline" message={thread.error} className="mb-3" />}
            <ol role="log" aria-label="Messages" className="space-y-2">
              {thread.messages.map((message, index) => {
                const previous = thread.messages[index - 1]
                const mine = message.sender.userId === viewer?.userId
                return (
                  <li key={message.localId ?? message.id}>
                    <MessageBubble
                      message={message}
                      mine={mine}
                      // A message still being sent has no colour of its own yet: the member list knows its author's.
                      tone={message.sender.tone ?? conversation?.members.find((m) => m.userId === message.sender.userId)?.tone}
                      showSender={shared && previous?.sender.userId !== message.sender.userId}
                      onRetry={thread.retry}
                      claim={conversation ? claimViewFor(message, conversation, viewer?.userId) : null}
                      claimBusy={claiming === message.id}
                      onClaim={() => void changeClaim(message.id, true)}
                      onRelease={() => void changeClaim(message.id, false)}
                      seen={conversation && message === lastSent ? seenLine(message, conversation, viewer?.userId) : null}
                    />
                  </li>
                )
              })}
            </ol>
          </>
        )}
      </div>

      {readOnly ? (
        <p className="flex shrink-0 items-center gap-2 border-t border-border px-4 py-3 text-sm text-muted-foreground" role="status">
          <Ban className="h-4 w-4 shrink-0" aria-hidden="true" />
          Ce compte est désactivé : la conversation est en lecture seule.
        </p>
      ) : (
        <>
          {conversation && (
            <QuickMessageBar
              conversation={conversation}
              viewerId={viewer?.userId}
              items={quickMessages.data?.items}
              onSend={sendQuick}
              onEdit={() => setEditing(true)}
            />
          )}
          <QuickMessagesEditorDialog open={editing} onOpenChange={setEditing} list={quickMessages.data} />
        </>
      )}

      {!readOnly && picker && (
        <PatientLinkPicker
          title={quickPending ? `${quickPending.label} — pour quel patient ?` : "Joindre un patient"}
          onCancel={() => setPicker(null)}
          onSkip={
            quickPending
              ? () => {
                  sendQuickWith(quickPending, null)
                  setPicker(null)
                }
              : undefined
          }
          onPick={(link) => {
            if (quickPending) sendQuickWith(quickPending, link)
            else setAttachment(link)
            setPicker(null)
          }}
        />
      )}

      {!readOnly && (
        <MessageComposer
          key={`${conversationKey}:${viewer?.userId ?? ""}`}
          conversation={conversationKey}
          viewerId={viewer?.userId}
          recipient={conversation?.title ?? "ce collègue"}
          onSend={thread.send}
          flush={Boolean(conversation && quickMessages.data) && !picker}
          attachment={attachment}
          onAttach={() => setPicker({ quick: null })}
          onClearAttachment={() => setAttachment(null)}
        />
      )}
    </>
  )
}
