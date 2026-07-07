# Auth0 Setup Guide

This guide will help you set up Auth0 authentication for the Clinic Management application.

## Prerequisites

1. An Auth0 account (sign up at https://auth0.com)
2. Access to Auth0 Dashboard

## Step 1: Create an Auth0 Application

1. Go to [Auth0 Dashboard](https://manage.auth0.com/)
2. Navigate to **Applications** > **Applications**
3. Click **Create Application**
4. Choose **Regular Web Applications**
5. Click **Create**

## Step 2: Configure Auth0 Application

1. In your application settings, configure:
   - **Allowed Callback URLs**: `http://localhost:3000/auth/callback`
   - **Allowed Logout URLs**: `http://localhost:3000`
   - **Allowed Web Origins**: `http://localhost:3000`
   - **Allowed Origins (CORS)**: `http://localhost:3000`

   **⚠️ CRITICAL for Auth0 v4**: 
   - The callback URL is `/auth/callback` (NOT `/api/auth/callback`)
   - Auth0 v4 uses `/auth/*` routes, not `/api/auth/*`
   - Make sure to add `http://localhost:3000/auth/callback` to your Allowed Callback URLs

2. Save the following values:
   - **Domain**: e.g., `your-tenant.auth0.com`
   - **Client ID**: Found in the application settings
   - **Client Secret**: Found in the application settings (click "Show" to reveal)

## Step 3: Create an Auth0 API

1. Navigate to **Applications** > **APIs**
2. Click **Create API**
3. Configure:
   - **Name**: Clinic Management API
   - **Identifier**: `https://clinic-management-api` (or your preferred identifier)
   - **Signing Algorithm**: RS256

4. Save the **Identifier** value (this is your API Audience)

## Step 4: Configure Frontend Environment Variables

1. Create or update `web/.env.local` with the following variables:

```env
# Auth0 Configuration (v4 uses different variable names)
AUTH0_DOMAIN=your-tenant.auth0.com
AUTH0_CLIENT_ID=your-client-id
AUTH0_CLIENT_SECRET=your-client-secret
AUTH0_SECRET=your-generated-secret-here
APP_BASE_URL=http://localhost:3000
AUTH0_AUDIENCE=https://clinic-management-api

# API URL
NEXT_PUBLIC_API_URL=http://localhost:5000/api
```

**Important**: In Auth0 v4, the environment variable names have changed:
- `AUTH0_DOMAIN` (instead of `AUTH0_ISSUER_BASE_URL`)
- `APP_BASE_URL` (instead of `AUTH0_BASE_URL`)

2. Generate a secure secret for `AUTH0_SECRET`:
   ```bash
   openssl rand -hex 32
   ```

## Step 5: Configure Backend (.NET API)

1. Open `api/ClinicManagement.API/appsettings.json`
2. Add/update the Auth0 configuration:

```json
{
  "Auth0": {
    "Domain": "your-tenant.auth0.com",
    "Audience": "https://clinic-management-api"
  }
}
```

## Step 5: Configure Frontend (Next.js)

1. Copy `.env.local.example` to `.env.local`:

```bash
cp web/.env.local.example web/.env.local
```

2. Generate a secret for session encryption (NOT the Auth0 client secret):

```bash
openssl rand -hex 32
```

3. Update `web/.env.local` with your Auth0 values:

```env
# Auth0 v4 Configuration (note the new variable names)
AUTH0_SECRET='your-generated-secret-here'
APP_BASE_URL='http://localhost:3000'
AUTH0_DOMAIN='your-tenant.auth0.com'
AUTH0_CLIENT_ID='your-client-id'
AUTH0_CLIENT_SECRET='your-client-secret'
AUTH0_AUDIENCE='https://clinic-management-api'
NEXT_PUBLIC_API_URL='http://localhost:5000/api'
```

**Important**: Auth0 v4 uses different environment variable names:
- `AUTH0_DOMAIN` (instead of `AUTH0_ISSUER_BASE_URL`)
- `APP_BASE_URL` (instead of `AUTH0_BASE_URL`)

### Important: Understanding the Secrets

**`AUTH0_SECRET`** (NOT the Auth0 client secret):
- This is a **session encryption secret** used by the Next.js Auth0 SDK
- It's used to sign and encrypt session cookies stored in the user's browser
- Generate a random 32-byte hex string (use `openssl rand -hex 32`)
- This secret is **only used server-side** in Next.js route handlers
- It's **never exposed to the browser/client**

**`AUTH0_CLIENT_SECRET`** (The actual Auth0 client secret):
- This is your Auth0 application's client secret
- Used to authenticate your Next.js application with Auth0's servers
- Also **only used server-side** in Next.js API routes
- Never exposed to the browser/client

**Why both secrets are safe:**
- Both secrets are stored in `.env.local` (not committed to git)
- They are **only accessible in server-side code** (Next.js API routes)
- They are **never sent to the browser** or exposed in client-side JavaScript
- Next.js automatically keeps server-only environment variables secure

## Step 6: Update CORS in Backend

The backend already has CORS configured to allow all origins. For production, you should restrict this to your frontend domain.

## Step 7: Test the Setup

1. Start the backend API:
```bash
cd api/ClinicManagement.API
dotnet run
```

2. Start the frontend:
```bash
cd web
npm run dev
```

3. Navigate to `http://localhost:3000`
4. You should be redirected to Auth0 login
5. After logging in, you should be redirected back to the application

## Troubleshooting

### 401 Unauthorized errors

- Verify that the Auth0 Domain and Audience in `appsettings.json` match your Auth0 configuration
- Check that the frontend is sending the access token in the Authorization header
- Verify that the API is configured to accept tokens from your Auth0 domain

### CORS errors

- Ensure that `http://localhost:3000` is in the allowed origins in Auth0
- Check that the backend CORS configuration allows requests from the frontend

### Token not being sent

- Check browser console for errors
- Verify that the user is authenticated (check `/api/auth/me`)
- Ensure the API client is using the access token

## Docker Deployment

When deploying with Docker, you need to provide Auth0 environment variables:

1. **Create a `.env.local` file** in the `web` directory:

```bash
cd web
cp .env.local.example .env.local
```

2. **Update `web/.env.local`** with your Auth0 values:

```env
# Auth0 v4 Configuration
AUTH0_SECRET=your-generated-secret-here
APP_BASE_URL=http://localhost:3000
AUTH0_DOMAIN=your-tenant.auth0.com
AUTH0_CLIENT_ID=your-client-id
AUTH0_CLIENT_SECRET=your-client-secret
AUTH0_AUDIENCE=https://clinic-management-api
NEXT_PUBLIC_API_URL=http://localhost:5000/api
```

**Note**: Auth0 v4 uses `AUTH0_DOMAIN` and `APP_BASE_URL` instead of the old variable names.

3. **The `docker-compose.yml` is already configured** to use `./web/.env.local`:

```yaml
web:
  env_file:
    - ./web/.env.local
```

4. **Build and run**:

```bash
docker-compose up --build
```

**Note**: 
- The `AUTH0_SECRET` error occurs when the application starts but Auth0 variables are not set
- Make sure `web/.env.local` exists with all required Auth0 environment variables
- The `.env.local` file is automatically ignored by git (should be in `.gitignore`)

## Production Deployment

For production:

1. Update Auth0 application settings with production URLs
2. Update `.env.local` (for local dev) or environment variables (for Docker/production) with production values
3. Update `appsettings.json` with production Auth0 configuration
4. Restrict CORS to production domain only
5. Use environment variables instead of hardcoded values
6. Never commit `.env` files with real secrets to version control

## Security Notes

- Never commit `.env.local` or `appsettings.json` with real credentials
- Use environment variables in production
- Rotate secrets regularly
- Use HTTPS in production
- Implement proper role-based access control (RBAC) if needed

