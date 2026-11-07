import Link from "next/link";
import { GuideRow, serverGet } from "@/lib/api";
import { getT, getLocale } from "@/lib/i18n-server";
import { localeFmt } from "@/lib/i18n";
import { cn } from "@/lib/utils";

function excerpt(body: string): string {
  const plain = body
    .replace(/^#{1,3}\s+/gm, "")
    .replace(/[*`]/g, "")
    .replace(/\[([^\]]+)\]\([^)]+\)/g, "$1")
    .trim();
  return plain.length > 180 ? plain.slice(0, 180) + "…" : plain;
}

export default async function GuidesPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const sp = await searchParams;
  const { t } = await getT();
  const locale = await getLocale();
  const fmt = localeFmt(locale);
  const cat = typeof sp.cat === "string" ? sp.cat : "";

  const guides = await serverGet<GuideRow[]>("/api/guides", 30)
    .catch(() => null);

  const cats = [...new Set((guides ?? []).map((g) => g.category))]
    .sort((a, b) => a.localeCompare(b));
  const visible = (guides ?? []).filter(
    (g) => !cat || g.category === cat,
  );

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <div>
        <h1 className="font-display text-3xl font-bold text-ink">
          {t("guides.title")}
        </h1>
        <p className="mt-1 max-w-xl text-sm text-dim">
          {t("guides.sub")}{" "}
          <Link href="/wiki" className="text-gold hover:underline">
            {t("guides.subWiki")}
          </Link>
          .
        </p>
      </div>

      {cats.length > 1 && (
        <nav
          aria-label={t("wiki.filters")}
          className="flex flex-wrap gap-1.5"
        >
          <Link
            href="/guides"
            className={cn(
              "rounded-md border px-3 py-1 text-xs transition-colors",
              !cat
                ? "border-gold/60 text-gold"
                : "border-raised text-mutedx hover:text-ink",
            )}
          >
            {t("guides.allCats")}
          </Link>
          {cats.map((c) => (
            <Link
              key={c}
              href={`/guides?cat=${encodeURIComponent(c)}`}
              className={cn(
                "rounded-md border px-3 py-1 text-xs transition-colors",
                cat === c
                  ? "border-crystal/70 text-crystal"
                  : "border-raised text-mutedx hover:text-ink",
              )}
            >
              {c}
            </Link>
          ))}
        </nav>
      )}

      {guides === null ? (
        <div className="panel px-5 py-10 text-center text-sm text-ember">
          {t("guides.unreachable")}
        </div>
      ) : guides.length === 0 ? (
        <div className="panel px-5 py-10 text-center text-sm text-dim">
          {t("guides.empty")}
        </div>
      ) : visible.length === 0 ? (
        <div className="panel px-5 py-10 text-center text-sm text-dim">
          {t("guides.emptyCat", { cat })}
        </div>
      ) : (
        <ul className="divide-y divide-raised/60">
          {visible.map((g) => (
            <li key={g.id}>
              <Link
                href={`/guides/${g.slug}`}
                className="group block py-4"
              >
                <div className="flex items-baseline justify-between gap-4">
                  <h2 className="min-w-0 truncate font-display text-base font-bold text-ink group-hover:text-gold">
                    {g.title}
                  </h2>
                  <span className="shrink-0 text-xs text-crystal">
                    {g.category}
                  </span>
                </div>
                <p className="mt-1 text-sm leading-relaxed text-dim">
                  {excerpt(g.body)}
                </p>
                <p className="mt-1 text-[11px] text-dim">
                  {t("common.by", {
                    name: g.author || t("guides.defaultAuthor"),
                  })}
                  {" · "}
                  {t("common.updated", { date: fmt.date(g.updatedAt) })}
                </p>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
