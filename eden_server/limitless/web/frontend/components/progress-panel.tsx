"use client";

import { useState } from "react";
import { apiGet } from "@/lib/api";
import { useT } from "@/components/i18n-provider";
import { Badge } from "@/components/ui/badge";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Skeleton } from "@/components/ui/skeleton";

interface AchRow {
  achievement_id: number; point: number; success_num: number;
  name?: string;
}
interface QuestRow { quest_id: number; status: number; name?: string }
// « Talents » d'un perso (WEB-API) : effects = points AA investis
// par effet (nom/classe résolus via talents.json) ; pools = points
// AA disponibles par classe (+1/niveau de classe).
interface TalentEffect {
  effect_id: number; points: number; class_id?: number | null;
  class_name?: string; name?: string; desc?: string;
}
interface TalentPool {
  class_id: number; class_level: number; aa_point: number;
  class_name?: string;
}
interface TalentsRes {
  effects: TalentEffect[]; pools: TalentPool[];
}

interface Progress {
  ach: AchRow[]; quests: QuestRow[]; talents: TalentsRes;
}

export function ProgressPanel({ charId, token }: {
  charId: number; token: string;
}) {
  const t = useT();
  const [open, setOpen] = useState(false);
  const [data, setData] = useState<Progress | null>(null);

  const toggle = async () => {
    const next = !open;
    setOpen(next);
    if (next && data === null) {
      try {
        const [ach, quests, talents] = await Promise.all([
          apiGet<AchRow[]>(`/api/progress/achievements/${charId}`, token),
          apiGet<QuestRow[]>(`/api/progress/quests/${charId}`, token),
          apiGet<TalentsRes>(`/api/progress/talents/${charId}`, token),
        ]);
        setData({ ach, quests, talents });
      } catch {
        setData({
          ach: [], quests: [],
          talents: { effects: [], pools: [] },
        });
      }
    }
  };

  return (
    <div className="filet-t pt-2">
      <button
        onClick={toggle}
        className="text-xs text-mutedx hover:text-gold"
      >
        {open
          ? `▾ ${t("dash.hideProgress")}`
          : `▸ ${t("dash.showProgress")}`}
      </button>
      {open && (
        data === null ? (
          <Skeleton className="mt-2 h-20 w-full" />
        ) : (
          <Tabs defaultValue="ach" className="mt-2">
            <TabsList className="h-8">
              <TabsTrigger value="ach" className="text-xs">
                {t("dash.tabAch", { n: data.ach.length })}
              </TabsTrigger>
              <TabsTrigger value="quests" className="text-xs">
                {t("dash.tabQuests", { n: data.quests.length })}
              </TabsTrigger>
              <TabsTrigger value="talents" className="text-xs">
                {t("dash.tabTalents", { n: data.talents.effects.length })}
              </TabsTrigger>
            </TabsList>
            <TabsContent value="ach">
              <div className="max-h-40 space-y-1 overflow-y-auto text-xs">
                {data.ach.length === 0 && (
                  <p className="text-dim">{t("dash.noAch")}</p>
                )}
                {data.ach.map((a) => (
                  <div key={a.achievement_id}
                    className="flex items-baseline justify-between gap-2">
                    <span className="truncate">
                      {a.name ?? `#${a.achievement_id}`}
                    </span>
                    <span className="shrink-0 text-dim">
                      {t("dash.talentPts", { n: a.point })}
                      {a.success_num > 1 && ` · ×${a.success_num}`}
                    </span>
                  </div>
                ))}
              </div>
            </TabsContent>
            <TabsContent value="quests">
              <div className="max-h-40 space-y-1 overflow-y-auto text-xs">
                {data.quests.length === 0 && (
                  <p className="text-dim">{t("dash.noQuests")}</p>
                )}
                {data.quests.map((q) => (
                  <div key={q.quest_id}
                    className="flex items-baseline justify-between gap-2">
                    <span className="truncate">
                      {q.name ?? `#${q.quest_id}`}
                    </span>
                    <Badge
                      variant={q.status >= 2 ? "default" : "secondary"}
                      className="h-4 shrink-0 text-[10px]"
                    >
                      {t.has(`dash.questStatus.${q.status}`)
                        ? t(`dash.questStatus.${q.status}`)
                        : `#${q.status}`}
                    </Badge>
                  </div>
                ))}
              </div>
            </TabsContent>
            <TabsContent value="talents">
              <div className="max-h-40 space-y-1 overflow-y-auto text-xs">
                {data.talents.pools.length > 0 && (
                  <p className="text-dim">
                    {data.talents.pools.map((p) => (
                      <span key={p.class_id} className="mr-3">
                        {p.class_name ?? `#${p.class_id}`} ·{" "}
                        {t("common.level", { n: p.class_level })} ·{" "}
                        {t("dash.talentPts", { n: p.aa_point })}
                      </span>
                    ))}
                  </p>
                )}
                {data.talents.effects.length === 0 && (
                  <p className="text-dim">{t("dash.noTalents")}</p>
                )}
                {data.talents.effects.map((tl) => (
                  <div key={tl.effect_id}
                    className="flex items-baseline justify-between gap-2">
                    <span className="truncate" title={tl.desc ?? undefined}>
                      {tl.name ?? `#${tl.effect_id}`}
                      {tl.class_name && (
                        <span className="ml-1 text-dim">
                          · {tl.class_name}
                        </span>
                      )}
                    </span>
                    <span className="shrink-0 text-dim">
                      {t("dash.talentPts", { n: tl.points })}
                    </span>
                  </div>
                ))}
              </div>
            </TabsContent>
          </Tabs>
        )
      )}
    </div>
  );
}
