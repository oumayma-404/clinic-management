FROM node:20-alpine AS base

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

# Set environment variable for API URL
ENV NEXT_PUBLIC_API_URL=http://localhost:5000/api

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


