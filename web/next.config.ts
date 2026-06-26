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
};

export default nextConfig;
