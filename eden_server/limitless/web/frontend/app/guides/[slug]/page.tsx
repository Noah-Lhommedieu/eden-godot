import Link from "next/link";
import { notFound } from "next/navigation";
import { GuideRow, serverGet } from "@/lib/api";
import { Markdown } from "@/components/markdown";
import { getT, getLocale } from "@/lib/i18n-server";
import { localeFmt } from "@/lib/i18n";

export default async function GuidePage({
  params,
}: {
  params: Promise<{ slug: string }>;
}) {
  const { slug } = await params;
  const { t } = await getT();
  const locale = await getLocale();
  const fmt = localeFmt(locale);
  const guide = await serverGet<GuideRow>(
    `/api/guides/${encodeURIComponent(slug)}`, 30,
  ).catch(() => null);
  if (guide === null) notFound();

  return (
    <div className="mx-auto max-w-3xl">
      <nav className="mb-4 text-xs text-dim">
        <Link href="/guides" className="hover:text-gold">
          {t("guides.breadcrumb")}
        </Link>
        {" / "}
        <span className="text-ink">{guide.title}</span>
      </nav>
      <header className="panel px-5 py-4">
        <p className="text-xs text-crystal">{guide.category}</p>
        <h1 className="mt-1 font-display text-2xl font-bold text-ink">
          {guide.title}
        </h1>
        <p className="mt-1 text-xs text-dim">
          {t("common.by", {
            name: guide.author || t("guides.defaultAuthor"),
          })}
          {" · "}
          {t("common.updated", { date: fmt.date(guide.updatedAt) })}
        </p>
      </header>
      <article className="panel mt-4 px-5 py-5">
        <Markdown text={guide.body} />
      </article>
    </div>
  );
}
