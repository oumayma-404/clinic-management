# Auth0 Secrets Explained

## Why Do We Need Secrets in the Frontend?

This is a common question, and the answer is: **We don't expose secrets to the browser!** All secrets are used **server-side only** in Next.js.

## How Next.js Handles Environment Variables

Next.js has two types of environment variables:

1. **Server-only variables** (default): Only accessible in server-side code
   - API routes (`/app/api/*`)
   - Server Components
   - Server Actions
   - Middleware

2. **Public variables** (prefixed with `NEXT_PUBLIC_`): Exposed to the browser
   - Client Components
   - Browser JavaScript

## The Two Secrets Explained

### 1. `AUTH0_SECRET` (Session Encryption Secret)

**What it is:**
- A random secret used by the Next.js Auth0 SDK
- Used to sign and encrypt session cookies
- Generated locally (not from Auth0)

**Why it's needed:**
- When a user logs in, Auth0 redirects back to your app with an authorization code
- Your Next.js API route exchanges this code for tokens
- The SDK stores the session in an encrypted cookie
- `AUTH0_SECRET` is used to encrypt/decrypt this cookie

**Where it's used:**
- Only in server-side code: `/app/api/auth/[...auth0]/route.ts`
- Never sent to the browser
- Never exposed in client-side JavaScript

**How to generate:**
```bash
openssl rand -hex 32
```

### 2. `AUTH0_CLIENT_SECRET` (Auth0 Application Secret)

**What it is:**
- Your Auth0 application's client secret
- Used to authenticate your app with Auth0's servers
- Found in your Auth0 Dashboard

**Why it's needed:**
- When exchanging the authorization code for tokens
- When refreshing access tokens
- To prove your app is legitimate

**Where it's used:**
- Only in server-side code: `/app/api/auth/[...auth0]/route.ts`
- Never sent to the browser
- Never exposed in client-side JavaScript

## Security Flow

```
User Browser                    Next.js Server                    Auth0
     |                               |                              |
     |---> Click Login ------------->|                              |
     |                               |---> Redirect to Auth0 ------>|
     |<--- Redirect to Auth0 --------|<--- Login Page --------------|
     |                               |                              |
     |---> Enter Credentials ------->|                              |
     |                               |                              |
     |<--- Redirect with Code ------|<--- Authorization Code -----|
     |                               |                              |
     |                               |---> Exchange Code + Secret ->|
     |                               |<--- Access Token ------------|
     |                               |                              |
     |<--- Encrypted Cookie ---------| (Uses AUTH0_SECRET)          |
     |                               |                              |
```

## What Gets Sent to the Browser?

**Nothing sensitive!** Only:
- Encrypted session cookie (can't be read without `AUTH0_SECRET`)
- Public user info (name, email, picture) - from the session
- Access token (only when making API calls, via server-side route)

## Verification

You can verify secrets are not exposed:

1. Open browser DevTools
2. Go to Network tab
3. Check any API request
4. You'll see the `Authorization: Bearer <token>` header
5. But you'll **never** see `AUTH0_SECRET` or `AUTH0_CLIENT_SECRET`

The access token is obtained server-side via `/api/auth/token` route, which uses the secrets internally.

## Best Practices

1. ✅ Keep `.env.local` in `.gitignore`
2. ✅ Never commit secrets to version control
3. ✅ Use different secrets for development and production
4. ✅ Rotate secrets periodically
5. ✅ Use environment variables in production (not hardcoded)

## Summary

- **`AUTH0_SECRET`**: Session encryption (generated locally)
- **`AUTH0_CLIENT_SECRET`**: Auth0 app authentication (from Auth0 Dashboard)
- Both are **server-only** and **never exposed to the browser**
- Next.js automatically keeps non-`NEXT_PUBLIC_` variables secure










