"use client";

// Tableau d'un board de classement — podium 1-3 + table dense +
// filtre d'affichage par classe (filtre les lignes affichées, le
// classement vient du serveur).
import { useState } from "react";
import { RankRow } from "@/lib/api";
import { GameIcon } from "@/components/icon-img";
import { useFmt, useT } from "@/components/i18n-provider";
import { cn } from "@/lib/utils";

const MEDAL = ["text-gold", "text-[#C9CEDD]", "text-[#C08A5A]"] as const;

export type ClassIcon = { id: number; icon?: string; name?: string };

function Podium({
  rows, classIcons,
}: {
  rows: RankRow[];
  classIcons: Map<number, ClassIcon>;
}) {
  const top = rows.slice(0, 3);
  if (top.length === 0) return null;
  const order = [top[1], top[0], top[2]].filter(Boolean);
  return (
    <div className="grid grid-cols-3 items-end gap-3">
      {order.map((r) => {
        const first = r.rank === 1;
        const cls = classIcons.get(r.classId);
        return (
          <div
            key={r.rank}
            className={cn(
              "panel flex flex-col items-center px-3 py-4 text-center",
              first && "border-gold/60",
            )}
          >
            <span
              className={cn(
                "font-display font-bold",
                MEDAL[r.rank - 1],
                first ? "text-3xl" : "text-xl",
              )}
            >
              {r.rank}
            </span>
            <GameIcon
              code={cls?.icon} name={r.name}
              size={first ? 56 : 44} className="my-2"
            />
            <p className="w-full truncate text-sm font-medium">{r.name}</p>
            <p className="text-xs text-dim">
              {cls?.name ?? `#${r.classId}`}
            </p>
            <p className="mt-1 font-display text-sm text-gold tabular-nums">
              {r.value.toLocaleString()}
            </p>
          </div>
        );
      })}
    </div>
  );
}

export function RankTable({
  rows, classList, levelLabel, unitLabel,
}: {
  rows: RankRow[];
  classList: ClassIcon[];
  levelLabel: string;
  unitLabel: string;
}) {
  const t = useT();
  const fmt = useFmt();
  const classIcons = new Map(classList.map((c) => [c.id, c]));
  const [clsFilter, setClsFilter] = useState("");

  const present = new Set(rows.map((r) => r.classId));
  const filterable = classList.filter((c) => present.has(c.id));
  const visible = clsFilter
    ? rows.filter((r) => String(r.classId) === clsFilter)
    : rows;

  return (
    <>
      {filterable.length > 1 && (
        <div className="mb-3 flex items-center gap-2">
          <label htmlFor="rank-cls" className="text-xs text-dim">
            {t("rank.filterClass")}
          </label>
          <select
            id="rank-cls"
            value={clsFilter}
            onChange={(e) => setClsFilter(e.target.value)}
            className="rounded-md border border-raised bg-night/60 px-2 py-1.5 text-sm text-ink"
          >
            <option value="">{t("rank.filterAll")}</option>
            {filterable.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name ?? `#${c.id}`}
              </option>
            ))}
          </select>
        </div>
      )}
      <Podium rows={visible} classIcons={classIcons} />
      <div className="panel mt-4 overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-raised text-left text-xs text-dim">
              <th className="w-14 px-4 py-2 font-normal">{t("rank.col.rank")}</th>
              <th className="px-4 py-2 font-normal">{t("rank.col.char")}</th>
              <th className="hidden px-4 py-2 font-normal sm:table-cell">
                {t("rank.col.class")}
              </th>
              <th className="px-4 py-2 text-right font-normal">{levelLabel}</th>
              <th className="px-4 py-2 text-right font-normal">{unitLabel}</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((r) => {
              const cls = classIcons.get(r.classId);
              return (
                <tr
                  key={`${r.rank}-${r.name}`}
                  className="cv-auto border-b border-raised/50 last:border-0"
                >
                  <td
                    className={cn(
                      "px-4 py-2 font-display",
                      r.rank <= 3 ? MEDAL[r.rank - 1] : "text-mutedx",
                    )}
                  >
                    {r.rank}
                  </td>
                  <td className="px-4 py-2">{r.name}</td>
                  <td className="hidden px-4 py-2 text-dim sm:table-cell">
                    <span className="inline-flex items-center gap-1.5">
                      <GameIcon code={cls?.icon} name={cls?.name} size={18} />
                      {cls?.name ?? r.classId}
                    </span>
                  </td>
                  <td className="px-4 py-2 text-right tabular-nums">
                    {r.level}
                  </td>
                  <td className="px-4 py-2 text-right font-medium tabular-nums">
                    {fmt.num(r.value)}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </>
  );
}
