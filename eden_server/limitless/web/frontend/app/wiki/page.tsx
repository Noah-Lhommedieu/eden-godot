import Link from "next/link";
import {
  DatasetMeta, Paged, WikiRow, serverGet,
} from "@/lib/api";
import { WikiSearch } from "@/components/wiki-search";
import { WikiFilters } from "@/components/wiki-filters";
import { WikiBrowser } from "@/components/wiki-browser";
import { DATASETS } from "@/lib/datasets";
import { getT } from "@/lib/i18n-server";
import { cn } from "@/lib/utils";

function WikiRail({
  kind, datasets, t,
}: {
  kind: string;
  datasets: DatasetMeta[];
  t: (k: string, v?: Record<string, string | number>) => string;
}) {
  const counts = new Map(datasets.map((d) => [d.id, d.count]));
  return (
    <aside className="panel h-fit p-3 lg:sticky lg:top-20">
      <p className="px-2 pb-2 text-xs text-dim">{t("wiki.datasets")}</p>
      <nav className="flex flex-wrap gap-1 lg:flex-col">
        {DATASETS.map((d) => (
          <Link
            key={d.id}
            href={`/wiki?kind=${d.id}`}
            className={cn(
              "flex items-baseline justify-between rounded-md px-2 py-1.5 text-sm transition-colors",
              d.id === kind
                ? "bg-raised text-gold"
                : "text-mutedx hover:bg-raised/50 hover:text-ink",
            )}
          >
            <span>{t(d.labelKey)}</span>
            <span className="ml-3 text-[11px] text-dim tabular-nums">
              {counts.get(d.id)?.toLocaleString() ?? "—"}
            </span>
          </Link>
        ))}
        <Link
          href="/wiki/talents"
          className="mt-1 flex items-baseline justify-between rounded-md border border-gold/40 px-2 py-1.5 text-sm text-gold hover:bg-gold/10"
        >
          {t("wiki.talentSim")}
        </Link>
        <Link
          href="/guides"
          className="flex items-baseline justify-between rounded-md border border-aurora/40 px-2 py-1.5 text-sm text-aurora hover:bg-aurora/10"
        >
          {t("wiki.guidesLink")}
        </Link>
      </nav>
    </aside>
  );
}

function Pagination({
  kind, q, f, page, size, total,
}: {
  kind: string; q: string; f: string[];
  page: number; size: number; total: number;
}) {
  const pages = Math.max(Math.ceil(total / size), 1);
  if (pages <= 1) return null;
  const mk = (p: number) => {
    const sp = new URLSearchParams({ kind });
    if (q) sp.set("q", q);
    for (const fl of f) sp.append("f", fl);
    if (p > 1) sp.set("page", String(p));
    return `/wiki?${sp}`;
  };
  const nums = new Set<number>([1, pages, page - 1, page, page + 1]);
  const sorted = [...nums].filter((p) => p >= 1 && p <= pages)
    .sort((a, b) => a - b);
  return (
    <nav className="mt-4 flex items-center gap-1 text-sm" aria-label="Pagination">
      {sorted.map((p, i) => (
        <span key={p} className="flex items-center gap-1">
          {i > 0 && sorted[i - 1] < p - 1 && (
            <span className="px-1 text-dim">…</span>
          )}
          <Link
            href={mk(p)}
            aria-current={p === page ? "page" : undefined}
            className={cn(
              "rounded-md px-2.5 py-1 tabular-nums",
              p === page
                ? "bg-raised text-gold"
                : "text-mutedx hover:bg-raised/50",
            )}
          >
            {p}
          </Link>
        </span>
      ))}
    </nav>
  );
}

export default async function WikiPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const sp = await searchParams;
  const { t } = await getT();
  const kind = typeof sp.kind === "string" &&
    DATASETS.some((d) => d.id === sp.kind)
    ? sp.kind : "items";
  const q = typeof sp.q === "string" ? sp.q : "";
  // `f` répétable (ET logique côté backend) — string ou string[].
  const f = Array.isArray(sp.f) ? sp.f
    : typeof sp.f === "string" ? [sp.f] : [];
  const page = Math.max(Number(sp.page) || 1, 1);
  const size = 60;
  const kindLabel = t(`ds.${kind}`);

  const [datasets, result] = await Promise.all([
    serverGet<DatasetMeta[]>("/api/wiki/datasets", 3600)
      .catch(() => []),
    serverGet<Paged<WikiRow>>(
      `/api/wiki/${kind}?page=${page}&size=${size}` +
        (q ? `&q=${encodeURIComponent(q)}` : "") +
        f.map((fl) => `&f=${encodeURIComponent(fl)}`).join(""),
      300,
    ).catch(() => null),
  ]);

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <h1 className="font-display text-3xl font-bold text-ink">
          {t("wiki.title")} — <span className="text-gold">{kindLabel}</span>
        </h1>
        <WikiSearch kind={kind} initial={q} f={f} />
      </div>

      <div className="grid gap-6 lg:grid-cols-[220px_1fr]">
        <WikiRail kind={kind} datasets={datasets} t={t} />

        <section className="min-w-0">
          <WikiFilters kind={kind} q={q} f={f} />
          <div className="mt-4">
            {result === null ? (
              <div className="panel px-5 py-10 text-center text-sm text-ember">
                {t("common.unreachableData")}
              </div>
            ) : result.items.length === 0 ? (
              <div className="panel px-5 py-10 text-center text-sm text-dim">
                {q || f.length > 0
                  ? t("common.noMatch", { q: q || f.join(" + ") })
                  : t("common.empty")}
              </div>
            ) : (
              <>
                <p className="mb-3 text-xs text-dim">
                  {q
                    ? t("common.entriesMatching", {
                        n: result.total, q,
                      })
                    : t("common.entries", { n: result.total })}
                </p>
                <WikiBrowser rows={result.items} kind={kind} />
                <Pagination
                  kind={kind} q={q} f={f} page={result.page}
                  size={result.size} total={result.total}
                />
              </>
            )}
          </div>
        </section>
      </div>
    </div>
  );
}
