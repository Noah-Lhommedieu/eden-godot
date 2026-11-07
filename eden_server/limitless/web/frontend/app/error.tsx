"use client";

import Link from "next/link";
import { useT } from "@/components/i18n-provider";

export default function GlobalError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  const t = useT();
  return (
    <div className="mx-auto mt-20 max-w-md text-center">
      <h1 className="font-display text-2xl font-bold text-ink">
        {t("error.title")}
      </h1>
      <p className="mt-3 text-sm text-dim">
        {error.message || t("error.sub")}
      </p>
      <div className="mt-6 flex justify-center gap-4">
        <button
          onClick={reset}
          className="rounded-md bg-gold px-4 py-2 text-sm font-semibold text-night"
        >
          {t("error.retry")}
        </button>
        <Link href="/" className="rounded-md border border-raised px-4 py-2 text-sm">
          {t("error.home")}
        </Link>
      </div>
    </div>
  );
}
