import Link from "next/link";
import { RankRow, WikiClass, serverGet } from "@/lib/api";
import { RankTable, ClassIcon } from "@/components/rank-table";
import { MyPlacements } from "@/components/my-placements";
import { getT } from "@/lib/i18n-server";
import { cn } from "@/lib/utils";

// Les 3 EXP du jeu sont des pistes distinctes — un onglet chacune,
// plus les boards PvP et succès. Les boards vides sont masqués par
// défaut (lien « afficher les vides » en fin d'onglets).
const BOARDS = [
  { id: "level", labelKey: "rank.board.level",
    unitKey: "rank.unit.exp", levelKey: "rank.col.charLv" },
  { id: "class", labelKey: "rank.board.class",
    unitKey: "rank.unit.exp", levelKey: "rank.col.classLv" },
  { id: "limitless", labelKey: "rank.board.limitless",
    unitKey: "rank.unit.exp", levelKey: "rank.col.ultLv" },
  { id: "pvp", labelKey: "rank.board.pvp",
    unitKey: "rank.unit.points", levelKey: "rank.col.charLv" },
  { id: "achievements", labelKey: "rank.board.achievements",
    unitKey: "rank.unit.points", levelKey: "rank.col.charLv" },
] as const;

type BoardId = (typeof BOARDS)[number]["id"];

const EMPTY_HINT: Record<BoardId, string> = {
  level: "",
  class: "rank.emptyClass",
  limitless: "rank.emptyLimitless",
  pvp: "rank.emptyPvp",
  achievements: "rank.emptyAch",
};

export default async function RankingsPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const sp = await searchParams;
  const { t } = await getT();
  const board = (BOARDS.some((b) => b.id === sp.board)
    ? sp.board : "level") as BoardId;
  const showEmpty = sp.showEmpty === "1";
  const meta = BOARDS.find((b) => b.id === board)!;

  // Tous les boards en parallèle — pour savoir lesquels sont vides
  // et les masquer (le cache API 60 s rend le coût négligeable).
  const [boards, classes] = await Promise.all([
    Promise.all(
      BOARDS.map((b) =>
        serverGet<RankRow[]>(`/api/rankings/${b.id}`, 60)
          .then((rows) => ({ id: b.id as BoardId, rows }))
          .catch(() => ({ id: b.id as BoardId, rows: null as RankRow[] | null })),
      ),
    ),
    serverGet<{ total: number; items: WikiClass[] }>(
      "/api/wiki/classes?size=400", 3600)
      .then((p) => p.items).catch(() => [] as WikiClass[]),
  ]);

  const rows = boards.find((b) => b.id === board)?.rows ?? null;
  const emptyBoards = boards.filter(
    (b) => b.rows !== null && b.rows.length === 0,
  );
  const classList: ClassIcon[] = classes.map((c) => ({
    id: c.id, icon: c.icon, name: c.name,
  }));

  const mk = (b: BoardId, se = showEmpty) =>
    `/rankings?board=${b}${se ? "&showEmpty=1" : ""}`;

  return (
    <div className="space-y-6">
      <h1 className="font-display text-3xl font-bold text-ink">
        {t("rank.title")}
      </h1>

      <nav className="flex flex-wrap items-center gap-1 border-b border-raised pb-3">
        {BOARDS.map((b) => {
          const isEmpty = emptyBoards.some((e) => e.id === b.id);
          if (isEmpty && !showEmpty && b.id !== board) return null;
          return (
            <Link
              key={b.id}
              href={mk(b.id)}
              title={isEmpty ? t("rank.hidden") : undefined}
              className={cn(
                "rounded-md px-3 py-1.5 text-sm transition-colors",
                b.id === board
                  ? "bg-raised text-gold"
                  : "text-mutedx hover:bg-raised/50 hover:text-ink",
                isEmpty && "opacity-50",
              )}
            >
              {t(b.labelKey)}
            </Link>
          );
        })}
        {emptyBoards.length > 0 && (
          <Link
            href={mk(board, !showEmpty)}
            className="ml-auto text-xs text-dim hover:text-gold"
          >
            {showEmpty
              ? t("rank.hideEmpty")
              : `${t("rank.showEmpty")} (${emptyBoards.length})`}
          </Link>
        )}
      </nav>

      {board === "class" && (
        <p className="text-xs text-dim">{t("rank.classNote")}</p>
      )}

      {rows === null ? (
        <div className="panel px-5 py-10 text-center text-sm text-ember">
          {t("rank.unreachable")}
        </div>
      ) : rows.length === 0 ? (
        <div className="panel px-5 py-10 text-center text-sm text-dim">
          {t("rank.empty", { hint: t(EMPTY_HINT[board]) })}
        </div>
      ) : (
        <>
          <RankTable
            rows={rows}
            classList={classList}
            levelLabel={t(meta.levelKey)}
            unitLabel={t(meta.unitKey)}
          />
          <MyPlacements rows={rows} />
        </>
      )}
    </div>
  );
}
