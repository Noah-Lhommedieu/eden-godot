"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { useT } from "@/components/i18n-provider";

/// Champ de recherche du wiki — met à jour `q` dans l'URL après
/// 350 ms de pause (recherche côté serveur via searchParams).
export function WikiSearch({
  kind,
  initial,
  f = [],
}: {
  kind: string;
  initial: string;
  f?: string[];
}) {
  const router = useRouter();
  const t = useT();
  const [value, setValue] = useState(initial);

  useEffect(() => setValue(initial), [initial]);

  useEffect(() => {
    if (value === initial) return;
    const tmr = setTimeout(() => {
      const params = new URLSearchParams({ kind });
      if (value.trim()) params.set("q", value.trim());
      for (const fl of f) params.append("f", fl);
      router.push(`/wiki?${params}`);
    }, 350);
    return () => clearTimeout(tmr);
  }, [value, initial, kind, f, router]);

  return (
    <input
      type="search"
      value={value}
      onChange={(e) => setValue(e.target.value)}
      placeholder={t("wiki.search")}
      aria-label={t("wiki.searchLabel")}
      className="w-full max-w-sm rounded-md border border-raised bg-night/60 px-3 py-2 text-sm text-ink placeholder:text-mutedx/70 focus-visible:outline-2 focus-visible:outline-gold"
    />
  );
}
