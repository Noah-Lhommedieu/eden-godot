"use client";

// Fournit t() + locale aux composants client — la locale et le dict
// viennent du layout serveur (cookie `locale`), donc le rendu SSR des
// composants client est déjà dans la bonne langue (pas de mismatch).
import { createContext, useContext, useMemo } from "react";
import {
  DATE_LOCALE, Dict, Locale, makeT, TFunc,
} from "@/lib/i18n";

type I18nCtx = { locale: Locale; t: TFunc; fmt: ReturnType<typeof mkFmt> };

function mkFmt(locale: Locale) {
  const tag = DATE_LOCALE[locale];
  return {
    num: (n: number) => n.toLocaleString(tag),
    date: (d: string | number | Date) =>
      new Date(d).toLocaleDateString(tag, {
        day: "numeric", month: "long", year: "numeric",
      }),
    dateTime: (d: string | number | Date) =>
      new Date(d).toLocaleString(tag),
  };
}

const Ctx = createContext<I18nCtx | null>(null);

export function I18nProvider({
  locale, dict, children,
}: {
  locale: Locale;
  dict: Dict;
  children: React.ReactNode;
}) {
  const value = useMemo<I18nCtx>(() => ({
    locale,
    t: makeT(dict as unknown as Record<string, unknown>),
    fmt: mkFmt(locale),
  }), [locale, dict]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useI18n(): I18nCtx {
  const ctx = useContext(Ctx);
  if (!ctx) {
    // Hors provider (ne devrait pas arriver) — repli FR brut.
    const t = ((k: string) => k) as TFunc;
    t.has = () => false;
    return { locale: "fr", t, fmt: mkFmt("fr") };
  }
  return ctx;
}

export function useT(): TFunc {
  return useI18n().t;
}

export function useLocale(): Locale {
  return useI18n().locale;
}

export function useFmt(): I18nCtx["fmt"] {
  return useI18n().fmt;
}
