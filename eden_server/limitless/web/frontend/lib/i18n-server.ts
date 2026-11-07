// i18n côté serveur uniquement — lit le cookie `locale` via
// next/headers (Next 16 : cookies() est async). Importable depuis
// les pages/layouts RSC ; jamais depuis un composant client.
import { cookies } from "next/headers";
import {
  DEFAULT_LOCALE, getDict, isLocale, Locale, LOCALE_COOKIE, makeT,
  TFunc,
} from "@/lib/i18n";

/** Locale courante — cookie `locale`, FR si absent/invalide. */
export async function getLocale(): Promise<Locale> {
  const store = await cookies();
  const v = store.get(LOCALE_COOKIE)?.value;
  return isLocale(v) ? v : DEFAULT_LOCALE;
}

/** t() côté serveur pour les pages/layouts RSC. */
export async function getT(): Promise<{ locale: Locale; t: TFunc }> {
  const locale = await getLocale();
  const dict = getDict(locale);
  return {
    locale,
    t: makeT(dict as unknown as Record<string, unknown>),
  };
}
