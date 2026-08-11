import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Shipped as its own container behind the private Caddy site (deploy/Caddyfile), exactly as `web/` is behind
  // the public one. `standalone` is what makes that image small enough to be worth building separately.
  output: "standalone",
};

export default nextConfig;
