"use client";

// Panneau Talents refait selon la maquette du jeu (lot12 W4) :
// rail gauche = liste des classes (icône + nom), panneau droit =
// 3 arbres horizontaux d'icônes avec rang « N/Max », boutons +/−,
// compteur « N KPs Remain / N KPs Entered », bouton Refresh,
// onglets Classic (S_Talent serveur) / Awaken (C_Talent client —
// classes éveillées ; le paramètre API reste variant=reforged)
// et onglet Certificates (certificats de la classe + combos).
import { useEffect, useMemo, useRef, useState } from "react";
import {
  apiGet, TalentClassEntry, TalentRules, TalentSlot, TalentTree,
  WikiCertificate, WikiClass,
} from "@/lib/api";
import { GameIcon } from "@/components/icon-img";
import { useT } from "@/components/i18n-provider";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Minus, Plus, RotateCcw } from "lucide-react";
import { cn } from "@/lib/utils";

// alloc[tree][effectId] = rang actuel du slot
type Alloc = Record<number, Record<number, number>>;
type Variant = "classic" | "reforged";
type Tab = "talents" | "certificates";

const FALLBACK_RULES: TalentRules = {
  pointsPerClassLevel: 1,
  maxCharacterLevel: 100,
  maxAaPoints: 120,
  costPerRank: [0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
    3, 3, 3, 3, 3],
  costTableTotal: [0, 1, 2, 3, 4, 5, 7, 9, 11, 13, 15, 17, 19, 21,
    23, 25, 28, 31, 34, 37, 40],
};

function costTotal(rules: TalentRules, rank: number): number {
  const tbl = rules.costTableTotal;
  return tbl[Math.min(Math.max(rank, 0), tbl.length - 1)] ?? rank;
}

function treeSpent(
  slots: TalentSlot[], a: Record<number, number>, rules: TalentRules,
) {
  return slots.reduce(
    (s, sl) => s + costTotal(rules, a[sl.effectId] ?? 0), 0);
}

function lockedReason(
  s: TalentSlot,
  a: Record<number, number>,
  spentInTree: number,
  byEffect: Map<number, TalentSlot>,
  t: (k: string, v?: Record<string, string | number>) => string,
): string | null {
  if (s.reqPoints && spentInTree < s.reqPoints) {
    return t("talent.needTreePoints", { n: s.reqPoints });
  }
  if (s.preEffect && (a[s.preEffect] ?? 0) < (s.prePoints ?? 1)) {
    const name = byEffect.get(s.preEffect)?.name ?? `#${s.preEffect}`;
    return t("talent.needPoints", { name, n: s.prePoints ?? 1 });
  }
  return null;
}

/* ----------------------- tuile d'un talent ----------------------- */

function TalentTile({
  slot, rank, lock, canUp, onAdd, onRemove,
}: {
  slot: TalentSlot; rank: number; lock: string | null;
  canUp: boolean;
  onAdd: () => void; onRemove: () => void;
}) {
  const t = useT();
  const maxed = rank >= slot.maxPts;
  const firstDesc = slot.desc?.split("\n")
    .find((l) => l.trim() && !l.startsWith("Max "));
  const tip = [
    `${slot.name ?? `Effect ${slot.effectId}`} — ${rank}/${slot.maxPts}`,
    firstDesc ?? "",
    lock ? `(${lock})` : "",
  ].filter(Boolean).join("\n");
  return (
    <div
      className={cn(
        "group relative flex w-14 flex-col items-center",
        lock && "opacity-45",
      )}
      title={tip}
    >
      <div className="relative">
        {slot.icon ? (
          <GameIcon
            code={slot.icon} name={slot.name ?? undefined} size={44}
            className={cn(
              "transition-[filter]",
              rank === 0 && "grayscale-[0.65] brightness-[0.72]",
              maxed && "border-gold",
            )}
          />
        ) : (
          <span
            aria-hidden
            className={cn(
              "game-icon inline-flex size-11 items-center justify-center text-xs",
              rank > 0 ? "text-gold" : "text-dim",
            )}
          >
            {slot.pos}
          </span>
        )}
        {/* Rang « N/Max » — badge collé au coin bas droit. */}
        <span
          className={cn(
            "absolute -bottom-1 -right-1 rounded-sm border px-1 text-[10px] font-semibold leading-4 tabular-nums",
            maxed
              ? "border-gold bg-gold text-night"
              : rank > 0
                ? "border-raised bg-night text-gold"
                : "border-raised bg-night/90 text-dim",
          )}
        >
          {rank}/{slot.maxPts}
        </span>
      </div>
      <div className="mt-1.5 flex items-center gap-0.5">
        <button
          type="button"
          onClick={onRemove}
          disabled={rank === 0}
          aria-label={t("talent.minus", { name: slot.name ?? "" })}
          className="inline-flex size-4 items-center justify-center rounded-sm border border-raised text-dim transition-colors hover:border-ember hover:text-ember disabled:opacity-30"
        >
          <Minus size={10} />
        </button>
        <button
          type="button"
          onClick={onAdd}
          disabled={!canUp}
          aria-label={t("talent.plus", { name: slot.name ?? "" })}
          className="inline-flex size-4 items-center justify-center rounded-sm border border-raised text-dim transition-colors hover:border-gold hover:text-gold disabled:opacity-30"
        >
          <Plus size={10} />
        </button>
      </div>
    </div>
  );
}

/* -------------------------- page ------------------------------- */

export default function TalentsPage() {
  const t = useT();
  const [classes, setClasses] = useState<TalentClassEntry[]>([]);
  const [classMeta, setClassMeta] = useState<Map<number, WikiClass>>(
    new Map());
  const [classId, setClassId] = useState<string>("");
  const [variant, setVariant] = useState<Variant>("classic");
  const [tab, setTab] = useState<Tab>("talents");
  const [tree, setTree] = useState<TalentTree | null>(null);
  const [rules, setRules] = useState<TalentRules>(FALLBACK_RULES);
  const [loadErr, setLoadErr] = useState(false);
  const [alloc, setAlloc] = useState<Alloc>({});
  const [level, setLevel] = useState(FALLBACK_RULES.maxCharacterLevel);
  const [copied, setCopied] = useState(false);
  const [certs, setCerts] = useState<WikiCertificate[] | null>(null);
  const pending = useRef<{
    classId: number; level: number; alloc: Alloc;
  } | null>(null);

  // ?build= « classId.level.tree.eff:pts,… » — appliqué à l'arrivée
  // de l'arbre (validation des effectIds réels).
  useEffect(() => {
    const b = new URLSearchParams(window.location.search).get("build");
    if (!b) return;
    const m = b.match(/^(\d+)\.(\d+)\.(.+)$/);
    if (!m) return;
    const cid = Number(m[1]);
    const lv = Math.min(
      Math.max(Number(m[2]) || 100, 1), FALLBACK_RULES.maxCharacterLevel);
    const a: Alloc = {};
    for (const part of m[3].split(",")) {
      const pm = part.match(/^(\d+)\.(\d+):(\d+)$/);
      if (!pm) continue;
      const tn = Number(pm[1]);
      const eff = Number(pm[2]);
      const pts = Math.max(Number(pm[3]) || 0, 0);
      if (pts > 0) a[tn] = { ...(a[tn] ?? {}), [eff]: pts };
    }
    pending.current = { classId: cid, level: lv, alloc: a };
    setLevel(lv);
    setClassId(String(cid));
  }, []);

  useEffect(() => {
    apiGet<TalentRules>("/api/talents/rules")
      .then(setRules).catch(() => {});
    apiGet<TalentClassEntry[]>(`/api/talents?variant=${variant}`)
      .then((r) => {
        setClasses(r);
        if (r.length > 0 && !pending.current) {
          setClassId((cur) =>
            r.some((c) => String(c.classId) === cur)
              ? cur : String(r[0].classId));
        }
      })
      .catch(() => setLoadErr(true));
    // Icônes/couleurs des classes pour le rail (dataset classes).
    apiGet<{ items: WikiClass[] }>("/api/wiki/classes?size=100")
      .then((r) =>
        setClassMeta(new Map(r.items.map((c) => [c.id, c]))))
      .catch(() => {});
  }, [variant]);

  useEffect(() => {
    if (!classId) return;
    setTree(null);
    setCerts(null);
    apiGet<TalentTree>(`/api/talents/${classId}?variant=${variant}`)
      .then((tr) => {
        setTree(tr);
        if (pending.current &&
            pending.current.classId === Number(classId)) {
          const valid: Alloc = {};
          const effMax = new Map<number, number>();
          for (const t2 of tr.trees) {
            for (const s of t2.slots) effMax.set(s.effectId, s.maxPts);
          }
          for (const [tn, slots] of Object.entries(
            pending.current.alloc)) {
            for (const [eff, pts] of Object.entries(slots)) {
              const max = effMax.get(Number(eff));
              if (max) {
                const tnN = Number(tn);
                valid[tnN] = {
                  ...(valid[tnN] ?? {}),
                  [Number(eff)]: Math.min(pts, max),
                };
              }
            }
          }
          setAlloc(valid);
          pending.current = null;
        }
      })
      .catch(() => setLoadErr(true));
    apiGet<{ items: WikiCertificate[] }>(
      `/api/wiki/certificates?f=${encodeURIComponent(`classId:${classId}`)}&size=100`)
      .then((r) => setCerts(r.items))
      .catch(() => setCerts([]));
  }, [classId, variant]);

  const budget = Math.min(
    level * rules.pointsPerClassLevel, rules.maxAaPoints);

  const totalSpent = useMemo(() => {
    if (!tree) return 0;
    let sum = 0;
    for (const tr of tree.trees) {
      for (const s of tr.slots) {
        sum += costTotal(rules, alloc[tr.tree]?.[s.effectId] ?? 0);
      }
    }
    return sum;
  }, [tree, alloc, rules]);

  const over = totalSpent > budget;
  const remain = Math.max(budget - totalSpent, 0);

  const buildString = useMemo(() => {
    const parts: string[] = [];
    for (const [tn, a] of Object.entries(alloc)) {
      for (const [e, v] of Object.entries(a)) {
        if (v > 0) parts.push(`${tn}.${e}:${v}`);
      }
    }
    return parts.length
      ? `${classId}.${level}.${parts.join(",")}`
      : "";
  }, [alloc, classId, level]);

  useEffect(() => {
    if (!classId) return;
    const sp = new URLSearchParams(window.location.search);
    if (buildString) sp.set("build", buildString);
    else sp.delete("build");
    const qs = sp.toString();
    window.history.replaceState(
      null, "",
      `${window.location.pathname}${qs ? `?${qs}` : ""}`,
    );
  }, [buildString, classId]);

  const add = (treeN: number, s: TalentSlot, a: Record<number, number>,
    spentInTree: number, byEffect: Map<number, TalentSlot>) => {
    const cur = a[s.effectId] ?? 0;
    if (cur >= s.maxPts) return;
    if (lockedReason(s, a, spentInTree, byEffect, t)) return;
    const nextCost =
      costTotal(rules, cur + 1) - costTotal(rules, cur);
    if (totalSpent + nextCost > budget) return;
    setAlloc({
      ...alloc,
      [treeN]: { ...a, [s.effectId]: cur + 1 },
    });
  };

  const remove = (treeN: number, s: TalentSlot) => {
    const a = { ...(alloc[treeN] ?? {}) };
    const cur = a[s.effectId] ?? 0;
    if (cur === 0) return;
    a[s.effectId] = cur - 1;
    const tr = tree?.trees.find((x) => x.tree === treeN);
    if (tr) {
      let changed = true;
      while (changed) {
        changed = false;
        for (const sl of tr.slots) {
          const need = sl.preEffect
            ? (a[sl.preEffect] ?? 0) >= (sl.prePoints ?? 1)
            : true;
          const treePts = treeSpent(tr.slots, a, rules);
          const okReq = sl.reqPoints ? treePts >= sl.reqPoints : true;
          if ((!need || !okReq) && (a[sl.effectId] ?? 0) > 0) {
            a[sl.effectId] = 0;
            changed = true;
          }
        }
      }
    }
    setAlloc({ ...alloc, [treeN]: a });
  };

  const share = async () => {
    const url = `${window.location.origin}/wiki/talents?build=` +
      buildString;
    try {
      await navigator.clipboard.writeText(url);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch { /* clipboard indisponible */ }
  };

  const sel = Number(classId);
  const selMeta = classMeta.get(sel);
  const clsLevel = Math.min(
    Math.max(level || 1, 1), rules.maxCharacterLevel);

  return (
    <div className="space-y-5">
      <div>
        <h1 className="font-display text-3xl font-bold text-ink">
          {t("talent.title")}
        </h1>
        <p className="mt-1 max-w-2xl text-sm text-dim">
          {t("talent.sub")}
        </p>
      </div>

      <div className="grid gap-5 lg:grid-cols-[230px_1fr]">
        {/* Rail des classes — icône + nom, sticky sur desktop. */}
        <aside className="panel h-fit p-2 lg:sticky lg:top-20">
          <ul className="grid max-h-[70vh] grid-cols-2 gap-0.5 overflow-y-auto sm:grid-cols-3 lg:grid-cols-1">
            {classes.map((c) => {
              const meta = classMeta.get(c.classId);
              const active = c.classId === sel;
              return (
                <li key={c.classId}>
                  <button
                    type="button"
                    onClick={() => {
                      setClassId(String(c.classId));
                      setAlloc({});
                    }}
                    aria-pressed={active}
                    className={cn(
                      "flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm transition-colors",
                      active
                        ? "bg-raised text-gold"
                        : "text-mutedx hover:bg-raised/50 hover:text-ink",
                    )}
                  >
                    <GameIcon
                      code={meta?.icon ?? null}
                      name={c.className ?? undefined} size={22}
                    />
                    <span className="min-w-0 truncate">
                      {c.className ?? `#${c.classId}`}
                    </span>
                  </button>
                </li>
              );
            })}
          </ul>
        </aside>

        {/* Panneau principal — onglets + arbres horizontaux. */}
        <section className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            {/* Onglets Talents / Certificates (comme le panneau du jeu). */}
            <div
              role="tablist"
              className="inline-flex overflow-hidden rounded-md border border-raised text-sm"
            >
              {(["talents", "certificates"] as Tab[]).map((tb) => (
                <button
                  key={tb}
                  type="button"
                  role="tab"
                  aria-selected={tab === tb}
                  onClick={() => setTab(tb)}
                  className={cn(
                    "px-4 py-1.5 transition-colors",
                    tab === tb
                      ? "bg-raised text-gold"
                      : "text-dim hover:text-ink",
                  )}
                >
                  {t(`talent.tab.${tb}`)}
                </button>
              ))}
            </div>
            {tab === "talents" && (
              <div
                role="tablist"
                aria-label={t("talent.variant")}
                className="inline-flex overflow-hidden rounded-md border border-raised text-sm"
              >
                {(["classic", "reforged"] as Variant[]).map((v) => (
                  <button
                    key={v}
                    type="button"
                    role="tab"
                    aria-selected={variant === v}
                    onClick={() => {
                      setVariant(v);
                      setAlloc({});
                    }}
                    className={cn(
                      "px-4 py-1.5 transition-colors",
                      variant === v
                        ? "bg-raised text-gold"
                        : "text-dim hover:text-ink",
                    )}
                  >
                    {t(`talent.variant.${v}`)}
                  </button>
                ))}
              </div>
            )}
            {selMeta && (
              <span className="ml-1 inline-flex items-center gap-2 text-sm text-dim">
                <GameIcon
                  code={selMeta.icon ?? null}
                  name={selMeta.name ?? undefined} size={22}
                />
                <span className="font-medium text-ink">
                  {selMeta.name ?? tree?.className}
                </span>
              </span>
            )}
          </div>

          {tab === "certificates" ? (
            <div className="mt-4">
              {certs === null ? (
                <div className="panel px-5 py-10 text-center text-sm text-dim">
                  {t("talent.loadingTree")}
                </div>
              ) : certs.length === 0 ? (
                <div className="panel px-5 py-10 text-center text-sm text-dim">
                  {t("talent.noCerts")}
                </div>
              ) : (
                <ul className="grid gap-3 sm:grid-cols-2">
                  {certs.map((c) => (
                    <li key={c.id} className="panel px-4 py-3">
                      <div className="flex items-center gap-2.5">
                        <GameIcon
                          code={c.icon ?? null} name={c.name ?? undefined}
                          size={32}
                        />
                        <div className="min-w-0">
                          <p className="truncate text-sm font-medium">
                            {c.name ?? c.id}
                          </p>
                          {c.stat && (
                            <p className="text-[11px] text-dim">{c.stat}</p>
                          )}
                        </div>
                      </div>
                      <ul className="mt-2 space-y-1">
                        {(c.levels ?? []).map((lv) => (
                          <li
                            key={lv.itemId}
                            className="flex items-baseline justify-between gap-2 text-xs"
                          >
                            <span className="text-mutedx">
                              {lv.name ?? `#${lv.itemId}`}
                            </span>
                            <span className="shrink-0 text-aurora">
                              {lv.buff?.effect ?? ""}
                            </span>
                          </li>
                        ))}
                      </ul>
                      {(c.combos ?? []).length > 0 && (
                        <ul className="mt-2 space-y-1 border-t border-raised/60 pt-2">
                          {(c.combos ?? []).map((cb, i) => (
                            <li key={i} className="text-xs">
                              <span className="text-gold">
                                {t("detail.certComboWith",
                                   { name: cb.with ?? "?" })}
                              </span>
                              {cb.name && ` — ${cb.name}`}
                              {cb.effect && (
                                <span className="text-aurora">
                                  {" "}{cb.effect}
                                </span>
                              )}
                            </li>
                          ))}
                        </ul>
                      )}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          ) : loadErr ? (
            <div className="panel mt-4 px-5 py-10 text-center text-sm text-ember">
              {t("talent.loadErr")}
            </div>
          ) : tree === null ? (
            <div className="panel mt-4 px-5 py-10 text-center text-sm text-dim">
              {classId
                ? t("talent.loadingTree") : t("talent.loadingClasses")}
            </div>
          ) : (
            <>
              {/* 3 arbres horizontaux — une ligne d'icônes par arbre,
                  comme le panneau de classe du jeu. */}
              <div className="mt-4 space-y-3">
                {tree.trees.map((tr) => {
                  const a = alloc[tr.tree] ?? {};
                  const byEffect = new Map(
                    tr.slots.map((s) => [s.effectId, s]));
                  const spentInTree = treeSpent(tr.slots, a, rules);
                  return (
                    <section key={tr.tree} className="panel px-4 py-3">
                      <div className="flex items-baseline justify-between">
                        <h2 className="font-display text-sm font-bold text-gold">
                          {t("talent.tree", { n: tr.tree })}
                        </h2>
                        <span className="text-[11px] text-dim tabular-nums">
                          {t("talent.pointsInTree", { n: spentInTree })}
                        </span>
                      </div>
                      <ul className="mt-3 flex flex-wrap gap-x-3 gap-y-4">
                        {tr.slots.map((s) => {
                          const cur = a[s.effectId] ?? 0;
                          const lock = lockedReason(
                            s, a, spentInTree, byEffect, t);
                          const nextCost =
                            costTotal(rules, cur + 1) -
                            costTotal(rules, cur);
                          const canUp = !lock && cur < s.maxPts &&
                            totalSpent + nextCost <= budget;
                          return (
                            <li key={s.effectId}>
                              <TalentTile
                                slot={s} rank={cur} lock={lock}
                                canUp={canUp}
                                onAdd={() =>
                                  add(tr.tree, s, a, spentInTree,
                                      byEffect)}
                                onRemove={() => remove(tr.tree, s)}
                              />
                            </li>
                          );
                        })}
                      </ul>
                    </section>
                  );
                })}
              </div>

              {/* Compteur de KPs + actions — la ligne basse de la
                  maquette (« N KPs Remain / N KPs Entered » +
                  Refresh). */}
              <div className="panel mt-3 flex flex-wrap items-center gap-x-6 gap-y-3 px-5 py-3">
                <div className="space-y-1">
                  <Label htmlFor="clv" className="text-xs">
                    {t("talent.classLevel")}
                  </Label>
                  <input
                    id="clv" type="number" min={1}
                    max={rules.maxCharacterLevel}
                    value={level}
                    title={t("talent.capClass")}
                    onChange={(e) =>
                      setLevel(Math.min(
                        Math.max(Number(e.target.value) || 1, 1),
                        rules.maxCharacterLevel))}
                    className="w-24 rounded-md border border-raised bg-night/60 px-3 py-1.5 text-sm"
                  />
                </div>
                <p className="text-sm">
                  <Badge
                    className={cn(
                      "mr-2 tabular-nums",
                      over ? "bg-ember text-night" : "bg-gold text-night",
                    )}
                  >
                    {t("talent.kpsRemain", { n: remain })}
                  </Badge>
                  <Badge variant="secondary" className="tabular-nums">
                    {t("talent.kpsEntered", { n: totalSpent })}
                  </Badge>
                  {over && (
                    <span className="ml-2 text-xs text-ember">
                      {t("talent.overBudget")}
                    </span>
                  )}
                </p>
                <div className="ml-auto flex gap-2">
                  <Button
                    variant="secondary" size="sm"
                    onClick={() => setAlloc({})}
                    disabled={totalSpent === 0}
                  >
                    <RotateCcw size={13} className="mr-1" />
                    {t("talent.refresh")}
                  </Button>
                  <Button
                    variant="secondary" size="sm"
                    onClick={share}
                    disabled={totalSpent === 0}
                  >
                    {copied ? t("talent.shareDone") : t("talent.share")}
                  </Button>
                </div>
              </div>
            </>
          )}
        </section>
      </div>
    </div>
  );
}
