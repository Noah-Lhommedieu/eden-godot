"use client";

import { useState } from "react";
import { cn } from "@/lib/utils";

/// Icône du jeu — public/icons/<CODE>.webp (DDS converties par
/// tools/export_web_data.py). Repli : médaillon avec l'initiale
/// quand le fichier est absent ou non référencé.
export function GameIcon({
  code,
  name,
  size = 40,
  className,
}: {
  code?: string | null;
  name?: string | null;
  size?: number;
  className?: string;
}) {
  const [err, setErr] = useState(false);
  const px = `${size}px`;
  if (!code || err) {
    return (
      <span
        aria-hidden
        className={cn(
          "game-icon inline-flex items-center justify-center text-dim",
          className,
        )}
        style={{ width: px, height: px, fontSize: size * 0.45 }}
      >
        {(name ?? "?").trim().charAt(0).toUpperCase() || "?"}
      </span>
    );
  }
  return (
    // eslint-disable-next-line @next/next/no-img-element
    <img
      src={`/icons/${code}.webp`}
      alt={name ?? ""}
      width={size}
      height={size}
      loading="lazy"
      onError={() => setErr(true)}
      className={cn("game-icon", className)}
    />
  );
}
