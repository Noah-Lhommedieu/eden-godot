// i18n partagé (serveur + client) — FR par défaut, EN et PT-BR.
// Le choix vit dans le cookie `locale` (posé par le sélecteur de
// langue dans la nav). Chaque libellé de l'UI passe par t("clé",
// {vars}) — les datasets du jeu restent en EN (libellés du client).
// ⚠ Pas d'import next/headers ici : ce module est aussi chargé par
// les composants client (I18nProvider). La lecture du cookie est
// dans lib/i18n-server.ts.
import fr from "@/locales/fr.json";
import en from "@/locales/en.json";
import br from "@/locales/br.json";

export const LOCALES = ["fr", "en", "br"] as const;
export type Locale = (typeof LOCALES)[number];
export const DEFAULT_LOCALE: Locale = "fr";
export const LOCALE_COOKIE = "locale";

export type Dict = typeof fr;
type AnyDict = Record<string, unknown>;

export const DICTS: Record<Locale, Dict> = { fr, en, br };

export function isLocale(v: string | undefined | null): v is Locale {
  return v === "fr" || v === "en" || v === "br";
}

export function getDict(locale: Locale): Dict {
  return DICTS[locale];
}

// Index aplati par dict — les locales mélangent clés imbriquées
// ("detail.q" objet) et clés plates à points ("color.yellow" dans
// "wiki"). On aplatit tout en chemins "a.b.c" → chaîne une fois par
// dict, puis la recherche est O(1) quel que soit le style de clé.
const flatCache = new WeakMap<AnyDict, Map<string, string>>();

function flatten(dict: AnyDict): Map<string, string> {
  let flat = flatCache.get(dict);
  if (flat) return flat;
  flat = new Map();
  const walk = (node: unknown, path: string) => {
    if (node === null || typeof node !== "object") {
      if (typeof node === "string" && path) flat!.set(path, node);
      return;
    }
    for (const [k, v] of Object.entries(node as AnyDict)) {
      walk(v, path ? `${path}.${k}` : k);
    }
  };
  walk(dict, "");
  flatCache.set(dict, flat);
  return flat;
}

/** Recherche "a.b.c" dans le dict ; remplace {var} par les vars.
 *  Repli : la clé elle-même (visible en dev — un oubli se voit). */
export function translate(
  dict: AnyDict,
  key: string,
  vars?: Record<string, string | number>,
): string {
  const cur = flatten(dict).get(key);
  if (typeof cur !== "string") return key;
  if (!vars) return cur;
  return cur.replace(/\{(\w+)\}/g, (_, name) =>
    vars[name] !== undefined ? String(vars[name]) : `{${name}}`,
  );
}

export type TFunc = ((
  key: string,
  vars?: Record<string, string | number>,
) => string) & {
  /** true si la clé existe — évite d'afficher une clé brute pour
   *  les libellés construits dynamiquement : `t.has(k) ? t(k) : fb` */
  has: (key: string) => boolean;
};

/** Fabrique un t() + has() sur un dict (clés plates ou imbriquées). */
export function makeT(dict: AnyDict): TFunc {
  const flat = flatten(dict);
  const fn = ((key: string, vars?: Record<string, string | number>) => {
    const cur = flat.get(key);
    if (typeof cur !== "string") return key;
    if (!vars) return cur;
    return cur.replace(/\{(\w+)\}/g, (_, name) =>
      vars[name] !== undefined ? String(vars[name]) : `{${name}}`,
    );
  }) as TFunc;
  fn.has = (key: string) => flat.has(key);
  return fn;
}

/** Locales BCP-47 pour toLocaleDateString / lang html. */
export const HTML_LANG: Record<Locale, string> = {
  fr: "fr",
  en: "en",
  br: "pt-BR",
};
export const DATE_LOCALE: Record<Locale, string> = {
  fr: "fr-FR",
  en: "en-US",
  br: "pt-BR",
};

/** Formatteurs liés à une locale (dates + nombres). */
export function localeFmt(locale: Locale) {
  const tag = DATE_LOCALE[locale];
  return {
    num: (n: number) => n.toLocaleString(tag),
    date: (d: string | number | Date) =>
      new Date(d).toLocaleDateString(tag, {
        day: "numeric",
        month: "long",
        year: "numeric",
      }),
    dateTime: (d: string | number | Date) =>
      new Date(d).toLocaleString(tag),
  };
}
