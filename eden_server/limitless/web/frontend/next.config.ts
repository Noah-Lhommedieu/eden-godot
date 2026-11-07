import type { NextConfig } from "next";

// API_INTERNAL_URL : côté serveur Next (rewrites + fetch RSC) —
// localhost:5154 en dev, http://api:8080 dans docker-compose.
const api = process.env.API_INTERNAL_URL ?? "http://localhost:5154";

const config: NextConfig = {
  output: "standalone",
  async rewrites() {
    return [
      { source: "/api/:path*", destination: `${api}/api/:path*` },
    ];
  },
};
export default config;
