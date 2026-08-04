"use client"

import { useState, useRef, useEffect, useCallback } from "react"
import { usePathname } from "next/navigation"
import { Button } from "@/components/ui/button"
import { Textarea } from "@/components/ui/textarea"
import { Card } from "@/components/ui/card"
// Using div with overflow instead of ScrollArea
import { Send, Bot, User, Loader2, X, Trash2, Mic, MicOff, Volume2, VolumeX, Square, WifiOff } from "lucide-react"
import { aiChatApi, type ChatMessage } from "@/lib/api/ai-chat"
import { patientsApi } from "@/lib/api/patients"
import type { PatientDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { useDoctors } from "@/lib/hooks/use-doctors"
import { useConnectivity } from "@/lib/connectivity/connectivity"
import { useSession } from "@/lib/auth/session"
import { isChromeLessPath } from "@/lib/nav"
import { toast } from "sonner"
import { cn } from "@/lib/utils"

/** How long the chat stays expanded after login before collapsing itself. */
const AUTO_OPEN_MS = 5000

/** Marks the post-login greeting as spent, so a refresh doesn't re-pop the panel. */
const AUTO_OPEN_ONCE_KEY = "clinic:ai-chat-greeted"

/**
 * Whether the assistant reads its replies aloud. `localStorage`, not `sessionStorage`, because AC-P3.25
 * requires the preference to survive a reload; **off** unless the value is exactly "1", so a corrupt or
 * absent value lands on the quiet default (AC-P3.24 — reading every reply aloud in a shared consultation
 * room, over a patient, was the § 7.4 complaint).
 */
const SPEECH_ENABLED_KEY = "clinic:ai-chat-speech"

// The chrome-less route list moved to `lib/nav.ts` — the assistant and P2's bottom bar must agree on which
// routes render no chrome, and two copies of that list is how they stop agreeing.

interface AIChatProps {
  className?: string
}

export function AIChat({ className }: AIChatProps) {
  const [messages, setMessages] = useState<ChatMessage[]>([
    {
      role: "assistant",
      content: "Bonjour ! Je suis votre assistant IA. Comment puis-je vous aider dans la gestion de la clinique aujourd'hui ?",
    },
  ])
  const [input, setInput] = useState("")
  const [isLoading, setIsLoading] = useState(false)
  // Collapsed by default: this is a global mount, so an expanded default meant the panel covered the
  // corner of every page on every load. It expands only on the post-login greeting or on user intent.
  const [isMinimized, setIsMinimized] = useState(true)
  const [isListening, setIsListening] = useState(false)
  const [isSpeechSupported, setIsSpeechSupported] = useState(false)
  const [isSpeaking, setIsSpeaking] = useState(false)
  // Off by default (AC-P3.24). Read from storage after mount so the server and client render the same thing.
  const [isSpeechEnabled, setIsSpeechEnabled] = useState(false)
  const [patients, setPatients] = useState<PatientDto[]>([])
  const { currentUserDoctor } = useDoctors()
  // AI chat calls HuggingFace via the server, so it needs internet. In Local mode when the server has
  // no internet, the widget degrades gracefully (AC-6.2); Cloud always reports online (R-3).
  const { internetReachable } = useConnectivity()
  const { user, isLoading: isSessionLoading } = useSession()
  const pathname = usePathname()
  const scrollAreaRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLTextAreaElement>(null)
  const recognitionRef = useRef<SpeechRecognition | null>(null)
  const synthRef = useRef<SpeechSynthesis | null>(null)
  const isManuallyStoppedRef = useRef(false)
  const autoCollapseTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  // The assistant has no business on screen before there is a session, or on the auth/onboarding
  // routes. Everything below keys off this so no work (and no doomed API call) happens while hidden.
  const isHidden = isSessionLoading || !user || isChromeLessPath(pathname)

  /** Drop the pending auto-collapse so the greeting timer never fights a deliberate open/close. */
  const cancelAutoCollapse = useCallback(() => {
    if (autoCollapseTimerRef.current) {
      clearTimeout(autoCollapseTimerRef.current)
      autoCollapseTimerRef.current = null
    }
  }, [])

  const handleCollapse = useCallback(() => {
    cancelAutoCollapse()
    setIsMinimized(true)
  }, [cancelAutoCollapse])

  const handleExpand = useCallback(() => {
    cancelAutoCollapse()
    setIsMinimized(false)
  }, [cancelAutoCollapse])

  useEffect(() => {
    // Auto-scroll to bottom when new messages arrive
    if (scrollAreaRef.current) {
      scrollAreaRef.current.scrollTop = scrollAreaRef.current.scrollHeight
    }
  }, [messages])

  // AC-P3.25 — restore the saved preference. Storage being unavailable (private mode) simply keeps the
  // quiet default rather than throwing on mount.
  useEffect(() => {
    try {
      setIsSpeechEnabled(window.localStorage.getItem(SPEECH_ENABLED_KEY) === "1")
    } catch {
      // Keep the default.
    }
  }, [])

  const toggleSpeech = useCallback(() => {
    setIsSpeechEnabled((prev) => {
      const next = !prev
      try {
        window.localStorage.setItem(SPEECH_ENABLED_KEY, next ? "1" : "0")
      } catch {
        // The toggle still applies for this session; only persistence is lost.
      }
      // Turning it off mid-sentence must silence the reply that is already playing, not just the next one.
      if (!next && synthRef.current) {
        synthRef.current.cancel()
        setIsSpeaking(false)
      }
      return next
    })
  }, [])

  // Load patients list for autocorrect. Gated on visibility: hooks still run when the component
  // renders null, so without this the widget fired a guaranteed-401 patients call on every /login load.
  useEffect(() => {
    if (isHidden) return
    const loadPatients = async () => {
      try {
        const patientsList = await patientsApi.list()
        setPatients(patientsList)
      } catch (error) {
        console.error("Failed to load patients for autocorrect:", error)
      }
    }
    loadPatients()
  }, [isHidden])

  // A brief hello after login so the assistant stays discoverable without being in the way:
  // expand, then collapse on its own. Spent once per tab session (sessionStorage) so a refresh or a
  // remount doesn't re-pop it; storage being unavailable simply skips the greeting.
  useEffect(() => {
    if (isHidden) return

    let alreadyGreeted = true
    try {
      alreadyGreeted = window.sessionStorage.getItem(AUTO_OPEN_ONCE_KEY) === "1"
      if (!alreadyGreeted) {
        window.sessionStorage.setItem(AUTO_OPEN_ONCE_KEY, "1")
      }
    } catch {
      // Private mode / storage disabled — skip rather than greet on every navigation.
    }
    if (alreadyGreeted) return

    setIsMinimized(false)
    autoCollapseTimerRef.current = setTimeout(() => {
      autoCollapseTimerRef.current = null
      setIsMinimized(true)
    }, AUTO_OPEN_MS)

    return cancelAutoCollapse
  }, [isHidden, cancelAutoCollapse])

  // Signed out: forget the greeting (and re-collapse) so the next login in this same tab greets again.
  useEffect(() => {
    if (isSessionLoading || user) return
    setIsMinimized(true)
    try {
      window.sessionStorage.removeItem(AUTO_OPEN_ONCE_KEY)
    } catch {
      // Storage unavailable — nothing to forget.
    }
  }, [isSessionLoading, user])

  // Initialize speech recognition and synthesis
  useEffect(() => {
    // Check if browser supports speech recognition
    const SpeechRecognition = window.SpeechRecognition || (window as any).webkitSpeechRecognition
    
    if (SpeechRecognition) {
      setIsSpeechSupported(true)
      const recognition = new SpeechRecognition()
      recognition.continuous = true // Keep listening until manually stopped
      recognition.interimResults = true // Show interim results as you speak
      recognition.lang = "fr-FR"

      let finalTranscript = ""

      recognition.onstart = () => {
        setIsListening(true)
        isManuallyStoppedRef.current = false
        finalTranscript = ""
      }

      recognition.onresult = (event: SpeechRecognitionEvent) => {
        let interimTranscript = ""
        
        // Combine all results
        for (let i = event.resultIndex; i < event.results.length; i++) {
          const transcript = event.results[i][0].transcript
          if (event.results[i].isFinal) {
            finalTranscript += transcript + " "
          } else {
            interimTranscript += transcript
          }
        }

        // Update input with final + interim results
        const currentText = finalTranscript + interimTranscript
        if (currentText.trim()) {
          setInput(currentText.trim())
        }
      }

      recognition.onerror = (event: SpeechRecognitionErrorEvent) => {
        console.error("Speech recognition error:", event.error)
        
        // Only stop if it's a critical error or manually stopped
        if (event.error === "not-allowed") {
          setIsListening(false)
          toast.error("Accès au microphone refusé", {
            description: "Autorisez l'accès au microphone pour utiliser la saisie vocale.",
          })
          recognitionRef.current?.stop()
        } else if (event.error === "aborted" && isManuallyStoppedRef.current) {
          // This is expected when manually stopped
          setIsListening(false)
        } else if (event.error !== "no-speech" && event.error !== "audio-capture") {
          // Don't stop for "no-speech" or "audio-capture" errors - these are common
          // Only show error for other issues
          console.warn("Speech recognition warning:", event.error)
        }
        // Don't stop recognition for minor errors - let it continue
      }

      recognition.onend = () => {
        // Only stop if manually stopped, otherwise restart
        if (isManuallyStoppedRef.current) {
          setIsListening(false)
          // Apply autocorrect when manually stopped
          if (finalTranscript.trim()) {
            const correctedText = autocorrectPatientNames(finalTranscript.trim())
            setInput(correctedText)
          }
        } else {
          // Recognition ended unexpectedly - restart it if we're still supposed to be listening
          if (isListening) {
            try {
              // Small delay before restarting
              setTimeout(() => {
                if (recognitionRef.current && !isManuallyStoppedRef.current) {
                  recognitionRef.current.start()
                }
              }, 100)
            } catch (error) {
              console.error("Error restarting speech recognition:", error)
              setIsListening(false)
            }
          }
        }
      }

      recognitionRef.current = recognition
    }

    // Initialize speech synthesis
    if (typeof window !== "undefined" && "speechSynthesis" in window) {
      synthRef.current = window.speechSynthesis
    }

    return () => {
      if (recognitionRef.current) {
        recognitionRef.current.stop()
      }
      if (synthRef.current) {
        synthRef.current.cancel()
      }
    }
  }, [])

  const handleSend = async () => {
    if (!input.trim() || isLoading) return

    // Engaged with the assistant — the greeting's auto-collapse must not fire under the conversation.
    cancelAutoCollapse()

    // Short-circuit when the server has no internet — the request would only fail (AC-6.2).
    if (!internetReachable) {
      toast.warning("Assistant IA indisponible", {
        description: "Une connexion internet est requise. L'assistant se réactivera automatiquement au retour de la connexion.",
      })
      return
    }

    // Apply autocorrect before sending (final check)
    const correctedInput = autocorrectPatientNames(input.trim())
    const finalInput = correctedInput !== input.trim() ? correctedInput : input.trim()
    
    // Update input if it was corrected
    if (correctedInput !== input.trim()) {
      setInput(correctedInput)
    }

    const userMessage: ChatMessage = {
      role: "user",
      content: finalInput,
    }

    const newMessages = [...messages, userMessage]
    setMessages(newMessages)
    setInput("")
    setIsLoading(true)

    try {
      const response = await aiChatApi.chat({
        messages: newMessages,
        context: {
          doctorId: currentUserDoctor?.id,
        },
      })

      const assistantMessage = { role: "assistant" as const, content: response.message }
      setMessages([...newMessages, assistantMessage])

      // AC-P3.24 — read aloud only when the user has asked for it. This used to fire on every reply for
      // anyone whose browser has speech synthesis, i.e. everyone.
      if (isSpeechEnabled && synthRef.current) {
        speakText(response.message)
      }
    } catch (error) {
      console.error("Failed to get AI response:", error)
      // A mid-request connectivity loss surfaces as ApiError(status === 0) from client.ts — give a
      // clear, retryable message instead of the generic failure toast (AC-6.5).
      if (error instanceof ApiError && error.status === 0) {
        toast.error("Connexion perdue", {
          description: "La connexion a été interrompue pendant l'envoi. Réessayez une fois la connexion rétablie.",
        })
      } else {
        toast.error("Échec de la réponse de l'assistant IA", {
          description: "Veuillez réessayer plus tard.",
        })
      }
      // Remove the user message on error
      setMessages(messages)
    } finally {
      setIsLoading(false)
      inputRef.current?.focus()
    }
  }

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault()
      handleSend()
    }
  }

  // Auto-resize textarea
  useEffect(() => {
    if (inputRef.current) {
      inputRef.current.style.height = "auto"
      inputRef.current.style.height = `${Math.min(inputRef.current.scrollHeight, 120)}px`
    }
  }, [input])

  const handleClear = () => {
    setMessages([
      {
        role: "assistant",
        content: "Bonjour ! Je suis votre assistant IA. Comment puis-je vous aider dans la gestion de la clinique aujourd'hui ?",
      },
    ])
  }

  const handleToggleListening = () => {
    if (!isSpeechSupported) {
      toast.error("Reconnaissance vocale non prise en charge", {
        description: "Votre navigateur ne prend pas en charge la reconnaissance vocale.",
      })
      return
    }

    if (!recognitionRef.current) return

    if (isListening) {
      // Stop listening manually
      isManuallyStoppedRef.current = true
      try {
        recognitionRef.current.stop()
        setIsListening(false)
        // Apply autocorrect to the final input
        if (input.trim()) {
          const correctedText = autocorrectPatientNames(input.trim())
          setInput(correctedText)
        }
      } catch (error) {
        console.error("Error stopping speech recognition:", error)
        setIsListening(false)
      }
    } else {
      // Start listening
      isManuallyStoppedRef.current = false
      try {
        recognitionRef.current.start()
      } catch (error: any) {
        // If already started, ignore the error
        if (error.name === "InvalidStateError") {
          // Recognition is already running, just update state
          setIsListening(true)
          isManuallyStoppedRef.current = false
        } else {
          console.error("Error starting speech recognition:", error)
          toast.error("Échec du démarrage de la saisie vocale", {
            description: "Veuillez réessayer.",
          })
        }
      }
    }
  }

  const speakText = (text: string) => {
    if (!synthRef.current) return

    // Cancel any ongoing speech
    synthRef.current.cancel()

    const utterance = new SpeechSynthesisUtterance(text)
    utterance.lang = "fr-FR"
    utterance.rate = 1.0
    utterance.pitch = 1.0
    utterance.volume = 1.0

    utterance.onstart = () => {
      setIsSpeaking(true)
    }

    utterance.onend = () => {
      setIsSpeaking(false)
    }

    utterance.onerror = (event) => {
      console.error("Speech synthesis error:", event)
      setIsSpeaking(false)
    }

    synthRef.current.speak(utterance)
  }

  const stopSpeaking = () => {
    if (synthRef.current) {
      synthRef.current.cancel()
      setIsSpeaking(false)
    }
  }

  // Calculate Levenshtein distance between two strings
  const levenshteinDistance = (str1: string, str2: string): number => {
    const len1 = str1.length
    const len2 = str2.length
    const matrix: number[][] = []

    for (let i = 0; i <= len1; i++) {
      matrix[i] = [i]
    }

    for (let j = 0; j <= len2; j++) {
      matrix[0][j] = j
    }

    for (let i = 1; i <= len1; i++) {
      for (let j = 1; j <= len2; j++) {
        if (str1[i - 1] === str2[j - 1]) {
          matrix[i][j] = matrix[i - 1][j - 1]
        } else {
          matrix[i][j] = Math.min(
            matrix[i - 1][j] + 1,     // deletion
            matrix[i][j - 1] + 1,     // insertion
            matrix[i - 1][j - 1] + 1  // substitution
          )
        }
      }
    }

    return matrix[len1][len2]
  }

  // Calculate similarity score (0-1, where 1 is identical)
  const similarity = (str1: string, str2: string): number => {
    const maxLen = Math.max(str1.length, str2.length)
    if (maxLen === 0) return 1
    const distance = levenshteinDistance(str1.toLowerCase(), str2.toLowerCase())
    return 1 - distance / maxLen
  }

  // Find the closest patient name match in text
  const findClosestPatientName = (text: string): { name: string; original: string; score: number } | null => {
    if (patients.length === 0) return null

    const textLower = text.toLowerCase().trim()
    // Normalize spaces and handle compound names (e.g., "Ben Khalifa" -> "Benkhalifa")
    const normalizedText = textLower.replace(/\s+/g, " ").trim()
    let bestMatch: { name: string; original: string; score: number } | null = null

    // Check each patient name
    for (const patient of patients) {
      const fullName = `${patient.firstName} ${patient.lastName}`
      const fullNameLower = fullName.toLowerCase()
      const fullNameNoSpaces = fullNameLower.replace(/\s+/g, "")
      const firstNameLower = patient.firstName.toLowerCase()
      const lastNameLower = patient.lastName.toLowerCase()
      const lastNameNoSpaces = lastNameLower.replace(/\s+/g, "")

      // Try to find patient name patterns in the text
      // Pattern 1: Full name appears as a phrase (exact match)
      if (normalizedText.includes(fullNameLower)) {
        const startIndex = normalizedText.indexOf(fullNameLower)
        const endIndex = startIndex + fullNameLower.length
        const original = text.substring(startIndex, endIndex)
        return { name: fullName, original, score: 1.0 }
      }

      // Pattern 1b: Full name without spaces (e.g., "OumaymaBenkhalifa")
      if (normalizedText.replace(/\s+/g, "").includes(fullNameNoSpaces)) {
        return { name: fullName, original: text, score: 0.95 }
      }

      // Pattern 2: Check similarity with full name
      const fullNameScore = similarity(normalizedText, fullNameLower)
      if (fullNameScore > 0.55 && (!bestMatch || fullNameScore > bestMatch.score)) {
        bestMatch = { name: fullName, original: text, score: fullNameScore }
      }

      // Pattern 2b: Check similarity with full name without spaces
      const fullNameNoSpacesScore = similarity(normalizedText.replace(/\s+/g, ""), fullNameNoSpaces)
      if (fullNameNoSpacesScore > 0.55 && (!bestMatch || fullNameNoSpacesScore > bestMatch.score)) {
        bestMatch = { name: fullName, original: text, score: fullNameNoSpacesScore }
      }

      // Pattern 3: Check if text contains first name and last name separately
      const words = normalizedText.split(/\s+/)
      let hasFirstName = false
      let hasLastName = false
      let firstNameWord = ""
      let lastNameWords: string[] = []

      // Check for first name in words
      for (const word of words) {
        const firstNameSimilarity = similarity(word, firstNameLower)
        if (firstNameSimilarity > 0.65) {
          hasFirstName = true
          firstNameWord = word
          break
        }
      }

      // Check for last name (could be multiple words like "Ben Khalifa")
      const remainingWords = words.filter(w => w !== firstNameWord)
      const remainingText = remainingWords.join(" ")
      const lastNameSimilarity = similarity(remainingText, lastNameLower)
      const lastNameNoSpacesSimilarity = similarity(remainingText.replace(/\s+/g, ""), lastNameNoSpaces)
      
      if (lastNameSimilarity > 0.6 || lastNameNoSpacesSimilarity > 0.6) {
        hasLastName = true
      }

      if (hasFirstName && hasLastName) {
        const combinedScore = 0.85
        if (!bestMatch || combinedScore > bestMatch.score) {
          bestMatch = { name: fullName, original: text, score: combinedScore }
        }
      }

      // Pattern 4: Check similarity with first name only (if it's a longer name)
      // Lower threshold for first name matching since it's more critical
      if (firstNameLower.length >= 3) {
        const firstNameScore = similarity(normalizedText.split(/\s+/)[0] || normalizedText, firstNameLower)
        if (firstNameScore > 0.6 && (!bestMatch || firstNameScore * 0.85 > bestMatch.score)) {
          // Also check if the remaining text might be the last name
          const remainingAfterFirst = normalizedText.substring(normalizedText.indexOf(" ") + 1) || ""
          if (remainingAfterFirst.length > 0) {
            const lastNameScore = similarity(remainingAfterFirst, lastNameLower) || 
                                 similarity(remainingAfterFirst.replace(/\s+/g, ""), lastNameNoSpaces)
            if (lastNameScore > 0.5) {
              bestMatch = { name: fullName, original: text, score: (firstNameScore + lastNameScore) / 2 }
            } else {
              bestMatch = { name: fullName, original: text, score: firstNameScore * 0.75 }
            }
          } else {
            bestMatch = { name: fullName, original: text, score: firstNameScore * 0.75 }
          }
        }
      }
    }

    return bestMatch && bestMatch.score > 0.55 ? bestMatch : null
  }

  // Autocorrect patient names in the transcribed text
  const autocorrectPatientNames = (text: string): string => {
    if (patients.length === 0) return text

    let correctedText = text
    // Split by word boundaries but keep the separators
    const tokens = text.match(/\b\w+\b|\s+/g) || []
    const corrections: Array<{ original: string; corrected: string; startIndex: number; endIndex: number }> = []

    // Try to match patient names in the text
    // Check 2-5 word combinations (patient names can be 2-4 words, plus we check overlapping)
    for (let i = 0; i < tokens.length; i++) {
      // Skip whitespace tokens
      if (/\s+/.test(tokens[i])) continue
      
      for (let len = 2; len <= 5 && i + len <= tokens.length; len++) {
        // Build phrase from tokens (skip whitespace-only tokens)
        const phraseTokens: string[] = []
        let tokenCount = 0
        let j = i
        
        while (j < tokens.length && tokenCount < len) {
          if (!/\s+/.test(tokens[j])) {
            phraseTokens.push(tokens[j])
            tokenCount++
          } else if (phraseTokens.length > 0) {
            // Include whitespace between words
            phraseTokens.push(tokens[j])
          }
          j++
        }
        
        const phrase = phraseTokens.join("").trim()
        if (phrase.length < 3) continue

        const match = findClosestPatientName(phrase)
        if (match && match.score > 0.55) {
          // Check if this phrase is likely a name (starts with capital letter or is a proper noun pattern)
          const firstChar = phrase[0]
          const isLikelyName = firstChar && (
            firstChar === firstChar.toUpperCase() || 
            /^[A-Z]/.test(phrase)
          )
          
          if (isLikelyName) {
            // Find the actual original text in the input
            const originalInText = phrase
            corrections.push({
              original: originalInText,
              corrected: match.name,
              startIndex: i,
              endIndex: j - 1
            })
            // Skip ahead to avoid overlapping matches
            i = j - 1
            break
          }
        }
      }
    }

    // Apply corrections (in reverse order to maintain indices)
    if (corrections.length > 0) {
      // Sort by start index in reverse order
      corrections.sort((a, b) => b.startIndex - a.startIndex)
      
      for (const correction of corrections) {
        // Use a more robust replacement that handles case variations
        const regex = new RegExp(correction.original.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'gi')
        correctedText = correctedText.replace(regex, correction.corrected)
      }

      // Show notification for the first correction
      const firstCorrection = corrections[corrections.length - 1]
      toast.info("Nom du patient corrigé", {
        description: `"${firstCorrection.original}" → "${firstCorrection.corrected}"`,
        duration: 2000,
      })
    }

    return correctedText
  }

  // Signed out, still resolving the session, or on an auth/onboarding route: render nothing at all.
  if (isHidden) {
    return null
  }

  if (isMinimized) {
    return (
      // Clears the bottom bar (AC-8). `--bottom-inset` is the bar plus the home indicator; above `md:` there
      // is no bar and the original `bottom-4` stands.
      <div className={cn("fixed bottom-[calc(1rem+var(--bottom-inset))] right-4 z-50 md:bottom-4", className)}>
        <Button
          onClick={handleExpand}
          className="rounded-full h-14 w-14 shadow-lg bg-primary hover:bg-primary/90"
          size="icon"
          title="Ouvrir l'assistant IA"
        >
          <Bot className="h-6 w-6" />
        </Button>
      </div>
    )
  }

  return (
    // AC-P3.16 — viewport-relative below `md:`. A fixed `w-96 h-[600px]` panel is wider than a 375px phone
    // and taller than its viewport, so on a phone the assistant covered the page and its input sat off-screen.
    // At `md:` and above the original geometry is unchanged.
    //
    // ⚠️ AC-P3.16's bottom offset is deliberately re-opened by AC-8: `bottom-4` now sat on top of the bottom
    // nav bar. It becomes `1rem + --bottom-inset` below `md:` — the same 1rem gap it always had, measured from
    // above the bar instead of from the viewport floor — and the available height drops by the same amount so
    // the panel still ends 1rem below the header rather than growing past it.
    <Card
      className={cn(
        "fixed bottom-[calc(1rem+var(--bottom-inset))] left-4 right-4 flex h-[70dvh] max-h-[calc(100dvh-2rem-var(--bottom-inset))] flex-col shadow-2xl z-50",
        "md:bottom-4 md:left-auto md:h-[600px] md:max-h-none md:w-96",
        className
      )}
    >
      {/* Header */}
      <div className="flex items-center justify-between p-4 border-b bg-primary text-primary-foreground rounded-t-lg">
        <div className="flex items-center gap-2">
          <Bot className="h-5 w-5" />
          <h3 className="font-semibold">Assistant IA</h3>
        </div>
        <div className="flex items-center gap-1">
          {/* AC-P3.26 — while a reply is playing, stopping it stays one click away. */}
          {isSpeaking && (
            <Button
              variant="ghost"
              size="sm"
              className="h-8 w-8 p-0 text-primary-foreground hover:bg-primary-foreground/20"
              onClick={stopSpeaking}
              title="Arrêter la lecture"
              aria-label="Arrêter la lecture"
            >
              <Square className="h-4 w-4" />
            </Button>
          )}
          {/* AC-P3.25 — the persistent, discoverable control. `aria-pressed` states which way it is set,
              since the icon alone cannot say so to a screen reader. */}
          <Button
            variant="ghost"
            size="sm"
            className="h-8 w-8 p-0 text-primary-foreground hover:bg-primary-foreground/20"
            onClick={toggleSpeech}
            aria-pressed={isSpeechEnabled}
            title={isSpeechEnabled ? "Désactiver la lecture à voix haute" : "Activer la lecture à voix haute"}
            aria-label={isSpeechEnabled ? "Désactiver la lecture à voix haute" : "Activer la lecture à voix haute"}
          >
            {isSpeechEnabled ? <Volume2 className="h-4 w-4" /> : <VolumeX className="h-4 w-4" />}
          </Button>
          <Button
            variant="ghost"
            size="sm"
            className="h-8 w-8 p-0 text-primary-foreground hover:bg-primary-foreground/20"
            onClick={handleClear}
            title="Effacer la conversation"
          >
            <Trash2 className="h-4 w-4" />
          </Button>
          {/* X collapses to the bubble — it used to be wired to "clear", which read as a dead close button. */}
          <Button
            variant="ghost"
            size="sm"
            className="h-8 w-8 p-0 text-primary-foreground hover:bg-primary-foreground/20"
            onClick={handleCollapse}
            title="Réduire l'assistant"
          >
            <X className="h-4 w-4" />
          </Button>
        </div>
      </div>

      {/* Messages */}
      <div className="flex-1 p-4 overflow-y-auto" ref={scrollAreaRef}>
        <div className="space-y-4">
          {messages.map((message, index) => (
            <div
              key={index}
              className={cn(
                "flex gap-3",
                message.role === "user" ? "justify-end" : "justify-start"
              )}
            >
              {message.role === "assistant" && (
                <div className="flex-shrink-0 w-8 h-8 rounded-full bg-primary/10 flex items-center justify-center">
                  <Bot className="h-4 w-4 text-primary" />
                </div>
              )}
              <div
                className={cn(
                  "rounded-lg px-4 py-2 max-w-[80%]",
                  message.role === "user"
                    ? "bg-primary text-primary-foreground"
                    : "bg-muted text-foreground"
                )}
              >
                <p className="text-sm whitespace-pre-wrap">{message.content}</p>
              </div>
              {message.role === "user" && (
                <div className="flex-shrink-0 w-8 h-8 rounded-full bg-primary/10 flex items-center justify-center">
                  <User className="h-4 w-4 text-primary" />
                </div>
              )}
            </div>
          ))}
          {isLoading && (
            <div className="flex gap-3 justify-start">
              <div className="flex-shrink-0 w-8 h-8 rounded-full bg-primary/10 flex items-center justify-center">
                <Bot className="h-4 w-4 text-primary" />
              </div>
              <div className="rounded-lg px-4 py-2 bg-muted">
                <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />
              </div>
            </div>
          )}
        </div>
      </div>

      {/* Input */}
      <div className="p-4 border-t">
        {/* `bg-warning-wash` + `text-warning-ink`, not `amber-50/700` with a hand-maintained `dark:` twin: the
            tokens already carry their dark values, so the twin is deleted rather than translated — a second,
            hand-kept dark mode is precisely what drifts. `text-warning-ink` and not `text-warning`, because
            `--warning` on its own wash lands near 3.5:1 and this is 12px text. */}
        {!internetReachable && (
          <div
            role="status"
            className="mb-2 flex items-center gap-2 rounded-md bg-warning-wash px-2 py-1.5 text-xs text-warning-ink"
          >
            {/* WifiOff, not MicOff — this banner is about connectivity, and the mic icon read as a
                microphone problem right next to the real mic button. */}
            <WifiOff className="h-3.5 w-3.5" />
            Connexion internet requise — l&apos;assistant IA est temporairement indisponible.
          </div>
        )}
        <div className="flex gap-2">
          {isSpeechSupported && (
            <Button
              onClick={handleToggleListening}
              disabled={isLoading || !internetReachable}
              size="icon"
              variant={isListening ? "destructive" : "outline"}
              /* Only the pulse. `variant="destructive"` already paints `bg-destructive text-white
                 hover:bg-destructive/90`, so `bg-red-500 hover:bg-red-600 text-white` was a hardcoded
                 re-implementation of the variant it sits on — one that ignores the theme and would survive a
                 change to `--destructive` as a divergent red next to every other refusal in the app. */
              className={cn(isListening && "animate-pulse")}
              title={isListening ? "Cliquez pour arrêter l'enregistrement" : "Cliquez pour la saisie vocale"}
            >
              {isListening ? (
                <MicOff className="h-4 w-4" />
              ) : (
                <Mic className="h-4 w-4" />
              )}
            </Button>
          )}
          <Textarea
            ref={inputRef}
            value={input}
            onChange={(e) => {
              // Someone is typing during the post-login greeting — stop the pending auto-collapse
              // so the panel can't close mid-sentence.
              cancelAutoCollapse()
              setInput(e.target.value)
            }}
            onKeyDown={handleKeyDown}
            placeholder={!internetReachable ? "Connexion internet requise…" : isListening ? "À l'écoute…" : "Écrivez votre message…"}
            disabled={isLoading || isListening || !internetReachable}
            className="flex-1 min-h-[40px] max-h-[120px] resize-none"
            rows={1}
          />
          <Button
            onClick={handleSend}
            disabled={!input.trim() || isLoading || isListening || !internetReachable}
            size="icon"
            className="bg-primary hover:bg-primary/90"
            aria-label="Envoyer le message"
          >
            {isLoading ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : (
              <Send className="h-4 w-4" />
            )}
          </Button>
        </div>
        {isListening && (
          <div className="mt-2 text-xs text-muted-foreground flex items-center gap-2">
            {/* `bg-destructive`, not `bg-red-500` — the recording dot and the mic button beside it are the same
                signal and must be the same red, which only holds if both read the token. */}
            <div className="size-2 animate-pulse rounded-full bg-destructive" />
            Enregistrement… Cliquez à nouveau sur le micro pour arrêter
          </div>
        )}
      </div>
    </Card>
  )
}

