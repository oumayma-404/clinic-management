import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: 'standalone',
  experimental: {
    serverActions: {
      bodySizeLimit: '2mb',
    },
  },
  // Skip static optimization for API routes
  skipTrailingSlashRedirect: true,
  // Exclude Auth0 routes from static analysis
  pageExtensions: ['tsx', 'ts', 'jsx', 'js'],
  // Disable ESLint during build (we can enable it later if needed)
  eslint: {
    ignoreDuringBuilds: true,
  },

  // Security headers for page responses (security-hardening US-12 / AC-12.6).
  //
  // CONDITIONED ON AUTH_MODE, and this is load-bearing (plan risk R-13). In Local mode Kestrel is the single
  // browser-facing endpoint and reverse-proxies pages to Next, so its SecurityHeadersMiddleware already
  // covers them. Emitting a second set here would give the browser TWO Content-Security-Policy headers, and
  // it then enforces the INTERSECTION of both rather than either policy — a page that passed the tested
  // policy could still break. So Next emits these only in Cloud, where it serves pages directly.
  async headers() {
    if (process.env.AUTH_MODE === 'local') {
      return [];
    }

    return [
      {
        source: '/:path*',
        headers: [
          { key: 'X-Content-Type-Options', value: 'nosniff' },
          { key: 'X-Frame-Options', value: 'DENY' },
          { key: 'Referrer-Policy', value: 'strict-origin-when-cross-origin' },
        ],
      },
    ];
  },
};

export default nextConfig;
