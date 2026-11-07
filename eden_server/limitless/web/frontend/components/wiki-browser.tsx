"use client";

// Navigateur de résultats wiki — trois modes d'affichage persistés
// (localStorage ll_wiki_view) : vignettes icônes / liste dense une
// colonne / tableau. Les libellés passent par t() ; les données du
// jeu restent en EN.
import Link from "next/link";
import { useEffect, useState } from "react";
import { WikiRow } from "@/lib/api";
import { EntityIcon } from "@/components/entity-icon";
import { useFmt, useT } from "@/components/i18n-provider";
import { cn } from "@/lib/utils";

type View = "grid" | "list" | "table";
const VIEW_KEY = "ll_wiki_view";
const VIEWS: View[] = ["grid", "list", "table"];

function rowIcon(r: WikiRow): string | undefined {
  return "icon" in r ? r.icon : undefined;
}
function rowName(r: WikiRow): string {
  return ("name" in r && r.name) || `#${r.id}`;
}
// Certificats : id chaîne « classId:Name » (pas d'id int — WEB-API).
function rowHref(r: WikiRow, kind: string): string {
  return `/wiki/${kind}/${encodeURIComponent(String(r.id))}`;
}
function rowRec(r: WikiRow): Record<string, unknown> {
  return r as unknown as Record<string, unknown>;
}

function qualityCls(r: WikiRow, kind: string): string {
  if (kind !== "items" && kind !== "certificates") return "";
  const q = (r as { quality?: number }).quality ?? 0;
  return q >= 2 ? `q${Math.min(q, 5)}` : "";
}
function qualityBorder(r: WikiRow, kind: string): string {
  if (kind !== "items" && kind !== "certificates") return "";
  const q = (r as { quality?: number }).quality ?? 0;
  return q >= 2 ? `qb${Math.min(q, 5)}` : "";
}

import { TFunc } from "@/lib/i18n";
type TFn = TFunc;

// Libellé dynamique sûr — clé connue → traduction, sinon valeur
// brute (jamais une clé « wiki.xxx » affichée).
function dyn(t: TFn, key: string, fallback: string): string {
  return t.has(key) ? t(key) : fallback;
}

// Pastille couleur de branche (certificats/classes) — rend le
// résultat du filtre `color:` lisible d'un coup d'œil (W2).
const BRANCH_DOT: Record<string, string> = {
  yellow: "bg-[#f0bc55]", orange: "bg-[#e8935a]", green: "bg-[#7fe3c6]",
  violet: "bg-[#b48af0]", blue: "bg-[#6ba8f0]",
};

export function BranchDot({ color }: { color?: string }) {
  if (!color || !BRANCH_DOT[color]) return null;
  return (
    <span
      aria-hidden
      className={`inline-block size-2 shrink-0 rounded-full ${BRANCH_DOT[color]}`}
      title={color}
    />
  );
}

function rowSub(r: WikiRow, kind: string, t: TFn): string {
  const rec = rowRec(r);
  const lv = (n?: number) => (n ? t("common.level", { n }) : "");
  switch (kind) {
    case "items":
      return [rec.category, lv(rec.reqLevel as number)]
        .filter(Boolean).join(" · ");
    case "certificates":
      return [rec.className, rec.stat,
        rec.color
          ? dyn(t, `wiki.color.${rec.color}`, String(rec.color))
          : ""]
        .filter(Boolean).join(" · ");
    case "monsters": {
      const m = rec as { level?: number; type?: number };
      const ty = m.type
        ? dyn(t, `wiki.mobType.${m.type}`, `Type ${m.type}`)
        : "";
      return [ty, lv(m.level)].filter(Boolean).join(" · ");
    }
    case "missions": {
      const m = rec as { type?: string; map?: string; minLevel?: number };
      return [m.type, m.map, m.minLevel ? `≥ ${m.minLevel}` : ""]
        .filter(Boolean).join(" · ");
    }
    case "spells": {
      const s = rec as { reqLevel?: number; mpCost?: number };
      return [lv(s.reqLevel), s.mpCost ? `${s.mpCost} MP` : ""]
        .filter(Boolean).join(" · ");
    }
    case "classes":
      return (rec.awakened as boolean)
        ? t("wiki.awakened.true") : t("wiki.awakened.false");
    case "achievements":
      return t("common.points", {
        n: (rec.points as number) ?? 0,
      });
    case "crafts":
      return t("detail.materials").toLowerCase() + " " +
        ((rec.materials as unknown[])?.length ?? 0);
    case "titles":
      return (rec.category as string) ?? "";
    case "npcs":
      return (rec.role as string) ?? "";
    case "maps":
      return (rec.type as number) === 3
        ? t("detail.town") : t("detail.zoneN", { n: rec.type as number });
    default:
      return "";
  }
}

function WikiTip({ r, kind }: { r: WikiRow; kind: string }) {
  const t = useT();
  const fmt = useFmt();
  const rec = rowRec(r);
  const num = (k: string) =>
    typeof rec[k] === "number" && (rec[k] as number) > 0
      ? (rec[k] as number) : undefined;
  const facts: [string, string | number][] = [];
  const push = (label: string, v?: string | number) => {
    if (v !== undefined && v !== "") facts.push([label, v]);
  };
  push("ID", r.id);
  if (kind === "certificates") {
    push(t("detail.certClass"), rec.className as string);
    push(t("wiki.stat"), rec.stat as string);
    push(t("wiki.branch"), rec.color
      ? dyn(t, `wiki.color.${rec.color}`, String(rec.color)) : undefined);
  } else if (kind === "items") {
    push(t("detail.category"), rec.category as string);
    push(t("common.levelShort"), num("reqLevel"));
    const stats = rec.stats as Record<string, number> | undefined;
    if (stats) {
      const top = Object.entries(stats)
        .filter(([, v]) => typeof v === "number" && v > 0)
        .slice(0, 3);
      for (const [k, v] of top) push(k.toUpperCase(), v);
    }
  } else if (kind === "monsters") {
    push(t("common.levelShort"), num("level"));
    push(t("detail.hp"), num("hp") ? fmt.num(num("hp")!) : undefined);
    push(t("detail.exp"), num("exp") ? fmt.num(num("exp")!) : undefined);
  } else if (kind === "spells") {
    push(t("common.levelShort"), num("reqLevel"));
    push("MP", num("mpCost"));
    push(
      t("detail.cooldown"),
      num("cooldownDs") ? `${num("cooldownDs")! / 10} s` : undefined,
    );
  } else if (kind === "achievements") {
    push(t("detail.achPoints"), num("points"));
  }
  return (
    <span className="wiki-tip" role="tooltip">
      <span className={cn("block text-sm font-medium", qualityCls(r, kind))}>
        {rowName(r)}
      </span>
      <span className="mt-1 grid grid-cols-[auto_1fr] gap-x-3 gap-y-0.5 text-xs">
        {facts.map(([k, v]) => (
          <span key={k} className="contents">
            <span className="text-dim">{k}</span>
            <span className="tabular-nums">{v}</span>
          </span>
        ))}
      </span>
    </span>
  );
}

function GridView({ rows, kind }: { rows: WikiRow[]; kind: string }) {
  return (
    <ul className="grid grid-cols-2 gap-2 sm:grid-cols-3 md:grid-cols-4 xl:grid-cols-5">
      {rows.map((r) => (
        <li key={r.id} className="group relative">
          <Link
            href={rowHref(r, kind)}
            className={cn(
              "panel-raised flex h-full items-center gap-2.5 p-2 transition-colors hover:border-gold/50",
              qualityBorder(r, kind),
            )}
          >
            <EntityIcon
              kind={kind} code={rowIcon(r)} name={rowName(r)}
              rec={rowRec(r)} size={36}
            />
            <span className="min-w-0">
              <span
                className={cn(
                  "block truncate text-sm",
                  qualityCls(r, kind),
                  "untranslated" in r && r.untranslated && "text-dim",
                )}
                title={rowName(r)}
              >
                {rowName(r)}
              </span>
              <SubText r={r} kind={kind} />
            </span>
          </Link>
          <WikiTip r={r} kind={kind} />
        </li>
      ))}
    </ul>
  );
}

function SubText({ r, kind }: { r: WikiRow; kind: string }) {
  const t = useT();
  const color = (kind === "certificates" || kind === "classes")
    ? (rowRec(r).color as string | undefined)
    : undefined;
  return (
    <span className="flex items-center gap-1 text-[11px] text-dim">
      <BranchDot color={color} />
      <span className="truncate">{rowSub(r, kind, t)}</span>
    </span>
  );
}

function ListView({ rows, kind }: { rows: WikiRow[]; kind: string }) {
  const t = useT();
  return (
    <ul className="divide-y divide-raised/60">
      {rows.map((r) => (
        <li key={r.id} className="cv-auto">
          <Link
            href={rowHref(r, kind)}
            className="flex items-center gap-3 py-2 hover:text-gold"
          >
            <EntityIcon
              kind={kind} code={rowIcon(r)} name={rowName(r)}
              rec={rowRec(r)} size={24}
            />
            <span
              className={cn(
                "min-w-0 flex-1 truncate text-sm",
                qualityCls(r, kind),
                "untranslated" in r && r.untranslated && "text-dim",
              )}
            >
              {rowName(r)}
            </span>
            <span className="shrink-0 text-xs text-dim">
              {rowSub(r, kind, t)}
            </span>
          </Link>
        </li>
      ))}
    </ul>
  );
}

// Colonnes du mode tableau — adaptées au dataset (les champs absents
// s'affichent « — »).
function tableCols(kind: string, t: TFn): [string, (r: WikiRow) => string][] {
  const rec = rowRec;
  const num = (r: WikiRow, k: string) => {
    const v = rec(r)[k];
    return typeof v === "number" && v > 0 ? String(v) : "—";
  };
  const str = (r: WikiRow, k: string) =>
    (rec(r)[k] as string) || "—";
  switch (kind) {
    case "items":
      return [
        [t("detail.category"), (r) => str(r, "category")],
        [t("common.levelShort"), (r) => num(r, "reqLevel")],
        [t("detail.quality"), (r) =>
          rec(r).quality
            ? dyn(t, `detail.q.${Math.min(rec(r).quality as number, 5)}`,
                `Q${rec(r).quality}`)
            : "—"],
      ];
    case "certificates":
      return [
        [t("detail.certClass"), (r) => str(r, "className")],
        [t("wiki.stat"), (r) => str(r, "stat")],
        [t("wiki.branch"), (r) =>
          rec(r).color
            ? dyn(t, `wiki.color.${rec(r).color}`, String(rec(r).color))
            : "—"],
      ];
    case "monsters":
      return [
        [t("common.levelShort"), (r) => num(r, "level")],
        [t("detail.type"), (r) =>
          rec(r).type
            ? dyn(t, `wiki.mobType.${rec(r).type}`, `Type ${rec(r).type}`)
            : "—"],
        [t("detail.hp"), (r) =>
          typeof rec(r).hp === "number" ? (rec(r).hp as number).toLocaleString() : "—"],
      ];
    case "missions":
      return [
        [t("detail.type"), (r) => str(r, "type")],
        [t("detail.zone"), (r) => str(r, "map")],
        [t("common.levelShort"), (r) => num(r, "minLevel")],
      ];
    case "spells":
      return [
        [t("common.levelShort"), (r) => num(r, "reqLevel")],
        [t("detail.mpCost"), (r) => num(r, "mpCost")],
        [t("detail.cooldown"), (r) =>
          typeof rec(r).cooldownDs === "number" && (rec(r).cooldownDs as number) > 0
            ? `${(rec(r).cooldownDs as number) / 10} s` : "—"],
      ];
    case "classes":
      return [
        [t("detail.tier"), (r) =>
          rec(r).awakened ? t("wiki.awakened.true") : t("wiki.awakened.false")],
        [t("detail.trait"), (r) => str(r, "trait")],
      ];
    case "achievements":
      return [
        [t("detail.achPoints"), (r) => num(r, "points")],
        [t("detail.objective"), (r) => num(r, "target")],
      ];
    case "titles":
      return [
        [t("detail.category"), (r) => str(r, "category")],
        [t("detail.bonus"), (r) => str(r, "bonus")],
      ];
    default:
      return [
        ["ID", (r) => String(r.id)],
      ];
  }
}

function TableView({ rows, kind }: { rows: WikiRow[]; kind: string }) {
  const t = useT();
  const cols = tableCols(kind, t);
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-raised text-left text-xs text-dim">
            <th className="w-14 px-3 py-2 font-normal">ID</th>
            <th className="px-3 py-2 font-normal"> </th>
            {cols.map(([h]) => (
              <th key={h} className="px-3 py-2 font-normal">{h}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.id} className="cv-auto border-b border-raised/50 last:border-0">
              <td className="px-3 py-1.5 text-xs text-dim tabular-nums">
                {r.id}
              </td>
              <td className="px-3 py-1.5">
                <Link
                  href={rowHref(r, kind)}
                  className={cn(
                    "inline-flex min-w-0 items-center gap-2 hover:text-gold",
                    qualityCls(r, kind),
                    "untranslated" in r && r.untranslated && "text-dim",
                  )}
                >
                  <EntityIcon
                    kind={kind} code={rowIcon(r)} name={rowName(r)}
                    rec={rowRec(r)} size={20}
                  />
                  <span className="truncate">{rowName(r)}</span>
                </Link>
              </td>
              {cols.map(([h, fn]) => (
                <td key={h} className="px-3 py-1.5 text-dim tabular-nums">
                  {fn(r)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function WikiBrowser({
  rows, kind,
}: {
  rows: WikiRow[];
  kind: string;
}) {
  const t = useT();
  const [view, setView] = useState<View>("grid");
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    const saved = window.localStorage.getItem(VIEW_KEY) as View | null;
    if (saved && VIEWS.includes(saved)) setView(saved);
    setMounted(true);
  }, []);

  const pick = (v: View) => {
    setView(v);
    window.localStorage.setItem(VIEW_KEY, v);
  };

  // Avant hydratation : rendu « grid » déterministe (le mode sauvé
  // s'applique au premier effet — une seule re-peinture).
  const effective = mounted ? view : "grid";

  return (
    <div>
      <div
        role="group"
        aria-label={t("wiki.viewLabel")}
        className="mb-3 inline-flex overflow-hidden rounded-md border border-raised text-xs"
      >
        {VIEWS.map((v) => (
          <button
            key={v}
            type="button"
            onClick={() => pick(v)}
            aria-pressed={effective === v}
            className={cn(
              "px-3 py-1.5 transition-colors",
              effective === v
                ? "bg-raised text-gold"
                : "text-dim hover:text-ink",
            )}
          >
            {t(`wiki.view${v[0].toUpperCase()}${v.slice(1)}`)}
          </button>
        ))}
      </div>
      {effective === "grid" && <GridView rows={rows} kind={kind} />}
      {effective === "list" && <ListView rows={rows} kind={kind} />}
      {effective === "table" && <TableView rows={rows} kind={kind} />}
    </div>
  );
}
