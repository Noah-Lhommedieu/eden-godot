"use client";

// Filtres du wiki — `f` répétable dans l'URL (ET logique, backend
// GameDataStore.Query : « champ:val », comparaison « champ>v »,
// chemins « maps.name », « drops.id », « materials.id »…). Chaque
// filtre actif devient un chip retirable ; la liste curée par
// dataset + un champ libre « champ[:op]:val » couvrent le reste.
import { useRouter } from "next/navigation";
import { useState } from "react";
import { KIND_FILTERS } from "@/lib/datasets";
import { useT } from "@/components/i18n-provider";

export function WikiFilters({
  kind, q, f,
}: {
  kind: string;
  q: string;
  f: string[];
}) {
  const router = useRouter();
  const t = useT();
  const groups = KIND_FILTERS[kind] ?? [];
  const [custom, setCustom] = useState("");

  const go = (filters: string[]) => {
    const sp = new URLSearchParams({ kind });
    if (q) sp.set("q", q);
    for (const fl of filters) sp.append("f", fl);
    router.push(`/wiki?${sp}`);
  };

  const add = (fl: string) => {
    const v = fl.trim();
    if (!v || f.includes(v)) return;
    go([...f, v]);
  };

  const remove = (fl: string) => go(f.filter((x) => x !== fl));

  const submitCustom = (e: React.FormEvent) => {
    e.preventDefault();
    if (custom.trim()) {
      add(custom);
      setCustom("");
    }
  };

  // Options encore non actives
  const unused = groups
    .map((g) => ({
      ...g,
      options: g.options.filter((o) => !f.includes(o.f)),
    }))
    .filter((g) => g.options.length > 0);

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-2 text-sm">
        <span className="text-xs text-dim">{t("wiki.filters")}</span>
        {unused.length > 0 && (
          <select
            aria-label={t("wiki.filterMore")}
            value=""
            onChange={(e) => {
              if (e.target.value) add(e.target.value);
            }}
            className="rounded-md border border-raised bg-night/60 px-2 py-1.5 text-sm text-ink"
          >
            <option value="">{t("wiki.filterMore")}</option>
            {unused.map((g) => (
              <optgroup key={g.groupKey} label={t(g.groupKey)}>
                {g.options.map((o) => (
                  <option key={o.f} value={o.f}>
                    {t(o.labelKey)}
                  </option>
                ))}
              </optgroup>
            ))}
          </select>
        )}
        <form onSubmit={submitCustom} className="flex items-center gap-2">
          <input
            value={custom}
            onChange={(e) => setCustom(e.target.value)}
            placeholder="champ:valeur"
            aria-label={t("wiki.filterValue")}
            className="w-40 rounded-md border border-raised bg-night/60 px-2 py-1.5 text-xs text-ink placeholder:text-mutedx/60"
          />
          {custom.trim() && (
            <button
              type="submit"
              className="rounded-md border border-raised px-2 py-1.5 text-xs text-gold hover:bg-raised/50"
            >
              {t("wiki.filterApply")}
            </button>
          )}
        </form>
      </div>
      {f.length > 0 && (
        <ul className="flex flex-wrap gap-1.5">
          {f.map((fl) => (
            <li key={fl}>
              <button
                type="button"
                onClick={() => remove(fl)}
                title={t("wiki.filterClear")}
                className="inline-flex items-center gap-1 rounded-md border border-crystal/50 bg-crystal/10 px-2 py-0.5 text-xs text-crystal hover:bg-crystal/20"
              >
                {fl}
                <span aria-hidden>×</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
