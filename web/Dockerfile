FROM node:22-alpine AS base

# Install dependencies only when needed
FROM base AS deps
RUN apk add --no-cache libc6-compat
WORKDIR /app

# Install dependencies based on the preferred package manager
COPY package.json package-lock.json* ./
RUN npm ci --legacy-peer-deps || npm install --legacy-peer-deps

# Rebuild the source code only when needed
FROM base AS builder
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .

# The API base URL, baked in by Next at build time — `NEXT_PUBLIC_*` is substituted into the bundle, not read at
# runtime, so it has to be a build ARG and not just an ENV.
#
# ⚠️ It was an `ENV` with no `ARG` for the whole life of this file, which made
# `deploy/docker-compose.prod.yml`'s `args: NEXT_PUBLIC_API_URL: /api` **silently do nothing** — Docker ignores a
# build arg the Dockerfile does not declare. Every image built from that compose baked
# `http://localhost:5000/api`, i.e. *the browser's own machine*, contradicting the comment beside the arg and
# AC-3's « no server address in the image ». The default is now the relative form the deployments actually want.
ARG NEXT_PUBLIC_API_URL=/api
ENV NEXT_PUBLIC_API_URL=${NEXT_PUBLIC_API_URL}

# The Graph API version the browser's Meta JS SDK is initialised with (`FB.init({ version })` in
# `components/reminder-settings.tsx`). A build ARG for the same reason as the URL above: `NEXT_PUBLIC_*` is
# substituted into the bundle by `npm run build`, so a runtime variable never reaches it.
#
# ⚠️ It exists because the version was pinned in **two independent places** — that constant and the server's
# `MetaConfig.DefaultGraphApiVersion` — with nothing deriving one from the other, so `Meta:GraphApiVersion` could
# advance the server's Graph calls while the browser SDK stayed behind. `deploy/docker-compose.hosted.yml` now
# feeds both from a single `META_GRAPH_API_VERSION` key. The default matches the server's.
ARG NEXT_PUBLIC_META_GRAPH_VERSION=v21.0
ENV NEXT_PUBLIC_META_GRAPH_VERSION=${NEXT_PUBLIC_META_GRAPH_VERSION}

# The Meta app and its Embedded Signup configuration, read by `lib/hooks/use-whatsapp-embedded-signup.ts`. Both are
# PUBLIC by design — they reach the browser bundle — so neither is a secret; `Meta__AppSecret` is server-side only
# and never appears here.
#
# ⚠️ These two were the **third** instance of this file's own trap: `deploy/docker-compose.hosted.yml` has passed
# them as build `args:` since `vendor-whatsapp-messaging-quota` Part 4, and with no `ARG` to receive them Docker
# discarded both — so every image built from that compose baked EMPTY strings. The failure is quiet in exactly the
# way the two blocks above describe: the hook returns early when `META_APP_ID` is empty, the Meta SDK is never
# injected, and « Connecter WhatsApp » answers « La connexion Meta n'est pas encore prête » for ever, which reads
# as a transient hiccup rather than as a build that never received the value.
#
# ⚠️ **No defaults**, unlike the two above: there is no sensible fallback for somebody else's Meta app, and an
# empty value is the honest state — the connect card is *absent* where the deployment cannot onboard, rather than
# present and permanently failing.
ARG NEXT_PUBLIC_META_APP_ID=
ENV NEXT_PUBLIC_META_APP_ID=${NEXT_PUBLIC_META_APP_ID}
ARG NEXT_PUBLIC_META_CONFIG_ID=
ENV NEXT_PUBLIC_META_CONFIG_ID=${NEXT_PUBLIC_META_CONFIG_ID}

# The SignalR hub's client log level, silent unless asked for. Found by the `next-public-build-args` check rather
# than by anybody noticing: `lib/realtime/clinic-hub.ts` states that « silencing it by default must not mean there
# is no way to see it », which was false in every containerised deployment — the one place a live realtime problem
# would need diagnosing. Empty keeps `LogLevel.None`.
ARG NEXT_PUBLIC_SIGNALR_LOG_LEVEL=
ENV NEXT_PUBLIC_SIGNALR_LOG_LEVEL=${NEXT_PUBLIC_SIGNALR_LOG_LEVEL}

# ⚠️ **`AUTH_MODE` must be set at BUILD time, not only at runtime, and this was a live defect.**
# `app/layout.tsx` is a server component that calls `resolveAuthMode()` to choose between `CloudSessionProvider`
# and `LocalSessionProvider`, and `/login` is **statically prerendered** — `.next/server/app/login.html` is
# emitted by `npm run build`. So the provider is baked into the static output using whatever `AUTH_MODE` held
# *during the build*, and a runtime `AUTH_MODE: local` never reaches it.
#
# `deploy/docker-compose.hosted.yml` sets it only at runtime, with a comment stating « Left at `cloud` the app
# expects Auth0 and there is no way to log in at all » — which is precisely the symptom it then shipped: the
# hosted multi-tenant deployment renders the Auth0 landing page and its « Se connecter » button goes nowhere.
# It still has to be a runtime variable too, because `middleware.ts` and the `/bff/auth/*` handlers read it per
# request; this ARG is in addition to that, not instead of it.
ARG AUTH_MODE=cloud
ENV AUTH_MODE=${AUTH_MODE}

# Set dummy Auth0 env vars for build (real values loaded at runtime from .env.local)
# Auth0 v4 uses different variable names
ENV AUTH0_SECRET=build-time-placeholder-build-time-placeholder-build-time-placeholder-build-time-placeholder
ENV APP_BASE_URL=http://localhost:3000
ENV AUTH0_DOMAIN=build-time-placeholder.auth0.com
ENV AUTH0_CLIENT_ID=build-time-placeholder
ENV AUTH0_CLIENT_SECRET=build-time-placeholder
ENV AUTH0_AUDIENCE=https://clinic-management-api

# Disable telemetry during the build
ENV NEXT_TELEMETRY_DISABLED 1

RUN npm run build

# Production image, copy all the files and run next
FROM base AS runner
WORKDIR /app

ENV NODE_ENV production
ENV NEXT_TELEMETRY_DISABLED 1

RUN addgroup --system --gid 1001 nodejs
RUN adduser --system --uid 1001 nextjs

COPY --from=builder /app/public ./public

# Set the correct permission for prerender cache
RUN mkdir .next
RUN chown nextjs:nodejs .next

# Automatically leverage output traces to reduce image size
COPY --from=builder --chown=nextjs:nodejs /app/.next/standalone ./
COPY --from=builder --chown=nextjs:nodejs /app/.next/static ./.next/static

USER nextjs

EXPOSE 3000

ENV PORT 3000
ENV HOSTNAME "0.0.0.0"

CMD ["node", "server.js"]


