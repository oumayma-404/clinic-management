import type { NextConfig } from "next";

/**
 * The console's own copy of the browser-protection headers (`hosted-security-hardening` FR-4.5).
 *
 * ⚠️ **Byte-identical to `deploy/Caddyfile`'s and to `SecurityHeadersMiddleware.ContentSecurityPolicy`**, and
 * `UnitTests/Common/ContentSecurityPolicyAgreementTests` fails the build if the three drift.
 *
 * ⚠️ **Both this and the Caddy block exist on purpose, and they do not duplicate each other.** Caddy *replaces*
 * a header the upstream sent, so in the deployed topology its copy is what a browser receives — but this
 * container is also run directly (`npm run dev`, `docker run console`), and there the proxy is not in the path
 * at all. The proxy's copy covers the deployment; this one covers the console being reached without it.
 */
const CSP =
  "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self'; object-src 'self' blob:; frame-src 'self' blob:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; report-uri /api/csp-report; report-to csp-endpoint";

const PERMISSIONS_POLICY =
  "accelerometer=(), autoplay=(), camera=(), display-capture=(), encrypted-media=(), fullscreen=(self), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), midi=(), payment=(), picture-in-picture=(), publickey-credentials-get=(), screen-wake-lock=(), usb=(), xr-spatial-tracking=()";

const nextConfig: NextConfig = {
  // Shipped as its own container behind the private Caddy site (deploy/Caddyfile), exactly as `web/` is behind
  // the public one. `standalone` is what makes that image small enough to be worth building separately.
  output: "standalone",
  async headers() {
    return [
      {
        source: "/:path*",
        headers: [
          { key: "X-Content-Type-Options", value: "nosniff" },
          { key: "X-Frame-Options", value: "DENY" },
          { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
          { key: "Permissions-Policy", value: PERMISSIONS_POLICY },
          { key: "Cross-Origin-Opener-Policy", value: "same-origin" },
          { key: "Cross-Origin-Resource-Policy", value: "same-site" },
          { key: "Content-Security-Policy", value: CSP },
        ],
      },
    ];
  },
};

export default nextConfig;
