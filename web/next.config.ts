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
  pageExtensions: ['tsx', 'ts', 'jsx', 'js'],
  // ⚠️ There is no `eslint` key here any more, and its absence is not a regression. Next 16 removed the built-in
  // ESLint integration outright (`next lint` is gone), so `eslint.ignoreDuringBuilds` is no longer a valid
  // NextConfig property — it is a `tsc --noEmit` error, which is how the upgrade surfaced it. Deleting it cannot
  // re-enable linting during the build, because Next 16 does not run ESLint during a build at all. `npm run lint`
  // remains unrunnable for its own separate reason: `eslint` is named in the script but is not in devDependencies.

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
