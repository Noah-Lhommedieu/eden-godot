"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import {
  apiGet, ApiError, CharacterRow, clearSession, LimitlessState,
  WikiClass,
} from "@/lib/api";
import { ProgressPanel } from "@/components/progress-panel";
import { useRequireAuth } from "@/lib/auth";
import { useRouter } from "next/navigation";
import { GameIcon } from "@/components/icon-img";
import { useFmt, useT } from "@/components/i18n-provider";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";

function LimitlessBar({ st }: { st?: LimitlessState }) {
  const t = useT();
  const fmt = useFmt();
  if (!st || (st.level === 0 && st.exp === 0)) {
    return <p className="text-xs text-dim">{t("dash.noUlt")}</p>;
  }
  const pct = st.expToNext
    ? Math.min((st.exp / st.expToNext) * 100, 100)
    : 0;
  return (
    <div>
      <div className="flex items-baseline justify-between text-xs">
        <span className="font-display font-bold text-aurora">
          {t("dash.ultLv", { n: st.level })}
        </span>
        <span className="text-dim tabular-nums">
          {fmt.num(st.exp)}
          {st.expToNext ? ` / ${fmt.num(st.expToNext)}` : " exp"}
        </span>
      </div>
      <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-raised/60">
        <div
          className="h-full rounded-full"
          style={{
            width: `${Math.max(pct, 2)}%`,
            background: "linear-gradient(90deg, var(--gold), var(--aurora))",
          }}
        />
      </div>
      {(st.perks ?? []).length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1">
          {st.perks.map((p) => (
            <span
              key={p.code}
              title={p.description}
              className="rounded border border-aurora/40 px-1.5 py-0.5 text-[10px] text-aurora"
            >
              {p.name} +{p.value}{p.valueKind === "mult" ? "%" : ""}
            </span>
          ))}
        </div>
      )}
    </div>
  );
}

export default function DashboardPage() {
  const { ok, token } = useRequireAuth();
  const router = useRouter();
  const t = useT();
  const fmt = useFmt();
  const [chars, setChars] = useState<CharacterRow[] | null>(null);
  const [ll, setLl] = useState<Record<number, LimitlessState>>({});
  const [classInfo, setClassInfo] = useState<
    Map<number, { icon?: string; name?: string }>
  >(new Map());
  const [err, setErr] = useState(false);

  useEffect(() => {
    apiGet<{ items: WikiClass[] }>("/api/wiki/classes?size=400")
      .then((p) => setClassInfo(new Map(
        p.items.map((c) => [c.id, { icon: c.icon, name: c.name }]),
      )))
      .catch(() => {});
  }, []);

  useEffect(() => {
    if (!token) return;
    apiGet<CharacterRow[]>("/api/account/characters", token)
      .then(async (rows) => {
        setChars(rows);
        const map: Record<number, LimitlessState> = {};
        await Promise.all(rows.map(async (c) => {
          try {
            map[c.id] = await apiGet<LimitlessState>(
              `/api/limitless/${c.id}`);
          } catch { /* no limitless data */ }
        }));
        setLl(map);
      })
      .catch((ex) => {
        // Token expiré/invalide → session nettoyée + retour login
        // plutôt qu'un écran vide (régression « perso invisible »).
        if (ex instanceof ApiError && ex.status === 401) {
          clearSession();
          router.replace("/login");
          return;
        }
        setErr(true);
      });
  }, [token, router]);

  if (!ok) return null;

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-3xl font-bold text-ink">
          {t("dash.title")}
        </h1>
        <Link
          href="/dashboard/new"
          className="rounded-md bg-gold px-4 py-2 font-semibold text-night hover:bg-gold/85"
        >
          {t("dash.new")}
        </Link>
      </div>

      {err ? (
        <div className="panel px-5 py-10 text-center text-sm text-ember">
          {t("dash.err")}
        </div>
      ) : chars === null ? (
        <div className="grid gap-4 md:grid-cols-2">
          <Skeleton className="h-44" />
          <Skeleton className="h-44" />
        </div>
      ) : chars.length === 0 ? (
        <div className="panel px-5 py-10 text-center text-sm text-dim">
          {t("dash.empty")}{" "}
          <Link href="/dashboard/new" className="text-gold hover:underline">
            {t("dash.emptyLink")}
          </Link>
          .
        </div>
      ) : (
        <div className="grid gap-4 md:grid-cols-2">
          {chars.map((c) => {
            const l = ll[c.id];
            const cls = classInfo.get(c.classId);
            return (
              <section key={c.id} className="panel px-5 py-4">
                <header className="flex items-center gap-3">
                  <GameIcon code={cls?.icon} name={c.name} size={44} />
                  <div className="min-w-0 flex-1">
                    <h2 className="truncate font-display text-lg font-bold">
                      {c.name}
                    </h2>
                    <p className="text-xs text-dim">
                      {cls?.name ?? t("dash.classN", { n: c.classId })}
                      {" · "}
                      {t("common.level", { n: c.lv })}
                    </p>
                  </div>
                  <Badge variant="secondary" className="tabular-nums">
                    {fmt.num(c.exp)} exp
                  </Badge>
                </header>
                <div className="mt-3">
                  <LimitlessBar st={l} />
                </div>
                <div className="mt-3">
                  <ProgressPanel charId={c.id} token={token!} />
                </div>
              </section>
            );
          })}
        </div>
      )}
    </div>
  );
}
