"use client";

// « Mes personnages » sous le tableau de classement — si connecté,
// affiche le rang de chacun de ses persos sur ce board (match par
// nom dans le top servi ; « hors top N » sinon).
import { useEffect, useState } from "react";
import Link from "next/link";
import { apiGet, CharacterRow, getToken, RankRow } from "@/lib/api";
import { useT } from "@/components/i18n-provider";
import { cn } from "@/lib/utils";

export function MyPlacements({ rows }: { rows: RankRow[] }) {
  const t = useT();
  const [chars, setChars] = useState<CharacterRow[] | null>(null);
  const [loggedOut, setLoggedOut] = useState(false);

  useEffect(() => {
    const token = getToken();
    if (!token) {
      setLoggedOut(true);
      return;
    }
    apiGet<CharacterRow[]>("/api/account/characters", token)
      .then(setChars)
      .catch(() => setChars([]));
  }, []);

  if (loggedOut) {
    return (
      <div className="panel mt-4 px-5 py-4 text-sm text-dim">
        {t("rank.mineEmpty")}{" "}
        <Link href="/login" className="text-gold hover:underline">
          {t("nav.signin")}
        </Link>
      </div>
    );
  }
  if (chars === null || chars.length === 0) return null;

  const byName = new Map(rows.map((r) => [r.name, r.rank]));
  return (
    <section className="panel mt-4 px-5 py-4">
      <h2 className="font-display text-base font-bold text-ink">
        {t("rank.mine")}
      </h2>
      <ul className="mt-2 divide-y divide-raised/50">
        {chars.map((c) => {
          const rank = byName.get(c.name);
          return (
            <li
              key={c.id}
              className="flex items-center justify-between gap-3 py-1.5 text-sm"
            >
              <span className="min-w-0 truncate">
                {c.name}
                <span className="ml-2 text-xs text-dim">
                  {t("common.level", { n: c.lv })}
                </span>
              </span>
              <span
                className={cn(
                  "shrink-0 text-xs tabular-nums",
                  rank ? "text-gold" : "text-dim",
                )}
              >
                {rank
                  ? t("rank.mineRanked", { rank })
                  : t("rank.mineUnranked", { n: rows.length })}
              </span>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
