import Link from "next/link";
import { Suspense } from "react";
import {
  GuideRow, NewsRow, RankRow, ServerStatus, serverGet,
} from "@/lib/api";
import { LimitlessLadder } from "@/components/limitless-ladder";
import { GameIcon } from "@/components/icon-img";
import { getT, getLocale } from "@/lib/i18n-server";
import { localeFmt } from "@/lib/i18n";
import { cn } from "@/lib/utils";

async function HomeData() {
  const [status, news, level, limitless, guides] = await Promise.all([
    serverGet<ServerStatus>("/api/admin/status", 20).catch(() => null),
    serverGet<NewsRow[]>("/api/admin/news", 60).catch(() => []),
    serverGet<RankRow[]>("/api/rankings/level", 60).catch(() => []),
    serverGet<RankRow[]>("/api/rankings/limitless", 60).catch(() => []),
    serverGet<GuideRow[]>("/api/guides", 60).catch(() => []),
  ]);
  return { status, news, level, limitless, guides };
}

function StatusStrip({
  status, t,
}: {
  status: ServerStatus | null;
  t: (k: string, v?: Record<string, string | number>) => string;
}) {
  const online = status?.online ?? 0;
  return (
    <div className="flex flex-wrap items-center gap-x-8 gap-y-2 border-b border-raised pb-4">
      <span
        className={cn(
          "inline-block size-2.5 rounded-full",
          status === null
            ? "bg-mutedx"
            : online > 0
              ? "live-dot bg-aurora"
              : "bg-ember",
        )}
      />
      <p className="text-sm text-dim">
        {t("home.playersOnline")}{" "}
        <span className="font-display text-xl font-bold text-ink">
          {status === null ? "—" : online.toLocaleString()}
        </span>
      </p>
      <p className="text-sm text-dim">
        {t("home.charsCreated")}{" "}
        <span className="font-display text-xl font-bold text-ink">
          {status === null ? "—" : status.total.toLocaleString()}
        </span>
      </p>
      <p className="ml-auto text-xs text-dim">
        {status === null
          ? t("home.statusDown")
          : online > 0
            ? t("home.worldLive")
            : t("home.worldEmpty")}
      </p>
    </div>
  );
}

function QuickAccess({
  t,
}: {
  t: (k: string, v?: Record<string, string | number>) => string;
}) {
  const links = [
    { href: "/wiki", title: t("home.quickWiki"), sub: t("home.quickWikiSub") },
    { href: "/rankings", title: t("home.quickRank"), sub: t("home.quickRankSub") },
    { href: "/wiki/talents", title: t("home.quickTalents"), sub: t("home.quickTalentsSub") },
    { href: "/dashboard", title: t("home.quickDash"), sub: t("home.quickDashSub") },
    { href: "/guides", title: t("home.quickGuides"), sub: t("home.quickGuidesSub") },
  ];
  return (
    <nav aria-label={t("home.quickTitle")} className="mt-6">
      <ul className="grid gap-x-8 sm:grid-cols-2">
        {links.map((l) => (
          <li key={l.href} className="border-b border-raised/50">
            <Link
              href={l.href}
              className="group flex items-baseline justify-between gap-4 py-2.5"
            >
              <span className="min-w-0">
                <span className="block truncate text-sm font-medium text-ink group-hover:text-gold">
                  {l.title}
                </span>
                <span className="block truncate text-xs text-dim">
                  {l.sub}
                </span>
              </span>
              <span aria-hidden className="shrink-0 text-dim group-hover:text-gold">
                ⌁
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}

function TopPlayers({
  rows, classIcons, t, fmt,
}: {
  rows: RankRow[];
  classIcons: Map<number, { icon?: string; name?: string }>;
  t: (k: string, v?: Record<string, string | number>) => string;
  fmt: { num: (n: number) => string };
}) {
  if (rows.length === 0) {
    return <p className="py-4 text-sm text-dim">{t("home.topEmpty")}</p>;
  }
  return (
    <ol className="divide-y divide-raised/60">
      {rows.slice(0, 5).map((r) => {
        const cls = classIcons.get(r.classId);
        return (
          <li
            key={r.rank}
            className="flex items-center justify-between gap-3 py-1.5"
          >
            <span className="flex min-w-0 items-center gap-2 text-sm">
              <span className="w-5 font-display text-gold">{r.rank}</span>
              <GameIcon code={cls?.icon} name={cls?.name} size={22} />
              <span className="truncate">{r.name}</span>
            </span>
            <span className="shrink-0 text-xs text-dim tabular-nums">
              {t("common.level", { n: r.level })} · {fmt.num(r.value)}
            </span>
          </li>
        );
      })}
    </ol>
  );
}

async function Sections() {
  const { status, news, level, limitless, guides } = await HomeData();
  const { t } = await getT();
  const locale = await getLocale();
  const fmt = localeFmt(locale);
  const classes = await serverGet<{
    items: { id: number; icon?: string; name?: string }[];
  }>("/api/wiki/classes?size=400", 3600)
    .then((p) => p.items)
    .catch(() => []);
  const classIcons = new Map(
    classes.map((c) => [c.id, { icon: c.icon, name: c.name }]),
  );
  return (
    <>
      <StatusStrip status={status} t={t} />

      <div className="mt-8 grid gap-8 lg:grid-cols-2">
        <section>
          <div className="flex items-baseline justify-between">
            <h2 className="font-display text-lg font-bold text-ink">
              {t("home.ladderTitle")}
            </h2>
            <Link
              href="/rankings?board=limitless"
              className="text-xs text-gold hover:underline"
            >
              {t("rank.board.limitless")}
            </Link>
          </div>
          <div className="mt-3">
            <LimitlessLadder
              emptyLabel={t("home.ladderEmpty")}
              rows={limitless.slice(0, 6).map((r) => ({
                name: r.name, value: r.value, level: r.level,
              }))}
            />
          </div>
        </section>
        <section>
          <div className="flex items-baseline justify-between">
            <h2 className="font-display text-lg font-bold text-ink">
              {t("home.topTitle")}
            </h2>
            <Link href="/rankings" className="text-xs text-gold hover:underline">
              {t("nav.rankings")}
            </Link>
          </div>
          <div className="mt-3">
            <TopPlayers rows={level} classIcons={classIcons} t={t} fmt={fmt} />
          </div>
        </section>
      </div>

      <div className="mt-8 grid gap-8 lg:grid-cols-2">
        <section>
          <div className="flex items-baseline justify-between">
            <h2 className="font-display text-lg font-bold text-ink">
              {t("home.guidesTitle")}
            </h2>
            <Link href="/guides" className="text-xs text-gold hover:underline">
              {t("common.seeAll")}
            </Link>
          </div>
          {guides.length === 0 ? (
            <p className="mt-3 text-sm text-dim">{t("home.guidesEmpty")}</p>
          ) : (
            <ul className="mt-2 divide-y divide-raised/60">
              {guides.slice(0, 4).map((g) => (
                <li key={g.id}>
                  <Link
                    href={`/guides/${g.slug}`}
                    className="flex items-baseline justify-between gap-4 py-2"
                  >
                    <span className="min-w-0 truncate text-sm text-ink hover:text-gold">
                      {g.title}
                    </span>
                    <span className="shrink-0 text-xs text-crystal">
                      {g.category}
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </section>
        {news.length > 0 && (
          <section>
            <h2 className="font-display text-lg font-bold text-ink">
              {t("home.newsTitle")}
            </h2>
            <div className="mt-3 space-y-4">
              {news.slice(0, 2).map((n) => (
                <article
                  key={n.id}
                  className="border-l-2 border-gold/50 pl-4"
                >
                  <h3 className="font-medium text-ink">{n.title}</h3>
                  <p className="text-xs text-dim">{fmt.date(n.created_at)}</p>
                  <p className="mt-1 whitespace-pre-wrap text-sm text-ink/85">
                    {n.body}
                  </p>
                </article>
              ))}
            </div>
          </section>
        )}
      </div>
    </>
  );
}

function SectionsFallback() {
  return (
    <>
      <div className="h-10 border-b border-raised" />
      <div className="mt-8 grid gap-8 lg:grid-cols-2">
        <div className="panel h-48 animate-pulse" />
        <div className="panel h-48 animate-pulse" />
      </div>
    </>
  );
}

export default async function Home() {
  const { t } = await getT();
  return (
    <div className="mx-auto max-w-4xl">
      {/* Hero — vraie illustration du jeu (écran de chargement
          Limestone Mtn.) sous un voile nuit ; le logo officiel et
          le titre la portent. Une seule audace : l'image. */}
      <section className="relative -mx-4 overflow-hidden rounded-xl border border-raised sm:-mx-6">
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          src="/art/hero.webp" alt=""
          className="absolute inset-0 h-full w-full object-cover object-[center_30%]"
        />
        <div
          aria-hidden
          className="absolute inset-0"
          style={{
            background:
              "linear-gradient(100deg, var(--night) 8%, " +
              "color-mix(in srgb, var(--night) 82%, transparent) 46%, " +
              "color-mix(in srgb, var(--night) 30%, transparent) 100%)",
          }}
        />
        <div className="relative flex items-center gap-6 px-6 py-10 sm:px-9 sm:py-12">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src="/art/logo.png" alt="Eden Eternal"
            className="hidden w-32 shrink-0 drop-shadow-[0_2px_10px_rgba(0,0,0,0.6)] sm:block md:w-40"
          />
          <div className="min-w-0 flex-1">
            <h1 className="font-display text-4xl font-bold text-ink drop-shadow-[0_2px_8px_rgba(0,0,0,0.7)]">
              {t("home.title")}
            </h1>
            <p className="mt-3 max-w-xl text-base text-ink/80 drop-shadow-[0_1px_6px_rgba(0,0,0,0.7)]">
              {t("home.tagline")}
            </p>
            <div className="mt-5 flex flex-wrap gap-3">
              <Link
                href="/register"
                className="rounded-md bg-gold px-5 py-2 text-sm font-semibold text-night hover:bg-gold/85"
              >
                {t("home.join")}
              </Link>
              <Link
                href="/wiki"
                className="rounded-md border border-ink/30 bg-night/40 px-5 py-2 text-sm font-semibold text-ink backdrop-blur-sm hover:border-gold/60 hover:text-gold"
              >
                {t("home.browseWiki")}
              </Link>
            </div>
          </div>
        </div>
      </section>

      <QuickAccess t={t} />

      <div className="mt-6">
        <Suspense fallback={<SectionsFallback />}>
          <Sections />
        </Suspense>
      </div>
    </div>
  );
}
