import Link from "next/link";
import { getT } from "@/lib/i18n-server";

export default async function NotFound() {
  const { t } = await getT();
  return (
    <div className="mx-auto mt-20 max-w-md text-center">
      <h1 className="font-display text-2xl font-bold text-ink">
        {t("error.notFound")}
      </h1>
      <p className="mt-3 text-sm text-dim">{t("error.notFoundSub")}</p>
      <Link
        href="/wiki"
        className="mt-6 inline-block rounded-md bg-gold px-4 py-2 text-sm font-semibold text-night"
      >
        {t("common.openWiki")}
      </Link>
    </div>
  );
}
