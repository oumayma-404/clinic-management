import { Auth0Client } from '@auth0/nextjs-auth0/server';

// Auth0 v4 - Initialize Auth0Client
// It will read configuration from environment variables
export const auth0 = new Auth0Client({
  authorizationParameters: {
    // Only include audience if it's set (for API access)
    // If not needed for basic auth, omit it to avoid consent screen
    ...(process.env.AUTH0_AUDIENCE && { audience: process.env.AUTH0_AUDIENCE }),
    scope: 'openid profile email',
    // Force login page instead of consent
    prompt: 'login',
  },
});







