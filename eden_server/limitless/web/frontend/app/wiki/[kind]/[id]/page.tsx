import Link from "next/link";
import { notFound } from "next/navigation";
import { serverGet, ApiError } from "@/lib/api";
import { GameIcon } from "@/components/icon-img";
import { EntityIcon, MapArt } from "@/components/entity-icon";
import { DATASETS } from "@/lib/datasets";
import { getT, getLocale } from "@/lib/i18n-server";
import { localeFmt, TFunc } from "@/lib/i18n";
import { cn } from "@/lib/utils";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type Row = Record<string, any>;

const STAT_LABEL: Record<string, string> = {
  hp: "HP", mp: "MP", str: "STR", agi: "AGI", int: "INT", wis: "WIS",
  lck: "LCK", patk: "P-ATK", matk: "M-ATK", def: "DEF", hit: "Hit",
  dodge: "Dodge", crit: "Crit", critDmg: "Crit dmg", mcrit: "M-Crit",
  mcritDmg: "M-Crit dmg", atkRange: "Range", atkSpeed: "Atk speed",
  element: "Element",
};

// Libellé dynamique sûr — la clé existe ? traduction : valeur
// brute (jamais une clé « wiki.xxx » affichée).
function dyn(t: TFunc, key: string, fallback: string): string {
  return t.has(key) ? t(key) : fallback;
}

function Field({
  label, value,
}: {
  label: string;
  value?: React.ReactNode;
}) {
  if (value === undefined || value === null || value === "" || value === 0)
    return null;
  return (
    <div className="flex justify-between gap-4 border-b border-raised/50 py-1.5 text-sm last:border-0">
      <span className="text-dim">{label}</span>
      <span className="text-right tabular-nums">{value}</span>
    </div>
  );
}

function Panel({
  title, children, className,
}: {
  title?: string;
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <section className={cn("panel px-5 py-4", className)}>
      {title && (
        <h2 className="font-display text-base font-bold text-gold">{title}</h2>
      )}
      <div className={title ? "mt-2" : undefined}>{children}</div>
    </section>
  );
}

function Description({ text, t }: { text?: string; t: TFunc }) {
  if (!text) return null;
  return (
    <Panel title={t("detail.description")} className="mt-4">
      <p className="whitespace-pre-wrap text-sm leading-relaxed text-ink/85">
        {text}
      </p>
    </Panel>
  );
}

// Liste d'items liés (drops, contenu de coffres) — icônes exportées
// + liens croisés vers /wiki/items/<id>, chance affichée si < 100 %.
type DropEntry = {
  id: number; name?: string; icon?: string; qty: number; chance: number;
};

function DropList({
  title, drops,
}: {
  title: string;
  drops?: DropEntry[];
}) {
  if (!drops?.length) return null;
  return (
    <Panel title={title} className="mt-4">
      <ul className="grid gap-2 sm:grid-cols-2">
        {drops.map((d) => (
          <li key={d.id}>
            <Link
              href={`/wiki/items/${d.id}`}
              className="panel-raised flex items-center gap-2.5 p-2 hover:border-gold/50"
            >
              <GameIcon code={d.icon} name={d.name} size={28} />
              <span className="min-w-0 flex-1 truncate text-sm">
                {d.name ?? `#${d.id}`}
              </span>
              <span className="shrink-0 text-xs text-dim tabular-nums">
                {d.qty > 1 ? `×${d.qty} ` : ""}
                {d.chance < 100 ? `${d.chance}%` : ""}
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </Panel>
  );
}

type MobRef = { id: number; name?: string };

function DroppedBy({ mobs, t }: { mobs?: MobRef[]; t: TFunc }) {
  if (!mobs?.length) return null;
  return (
    <Panel title={t("detail.droppedBy")} className="mt-4">
      <div className="flex flex-wrap gap-1.5">
        {mobs.map((m) => (
          <Link
            key={m.id}
            href={`/wiki/monsters/${m.id}`}
            className="rounded border border-raised px-2 py-0.5 text-xs text-mutedx hover:border-gold/50 hover:text-gold"
          >
            {m.name ?? `#${m.id}`}
          </Link>
        ))}
      </div>
    </Panel>
  );
}

// Grille de stats d'un item — la « colonne statistique » du nouveau
// détail : placée sous l'icône, avant les métadonnées.
function StatsGrid({
  stats, t, num,
}: {
  stats: Record<string, number | number[]>;
  t: TFunc;
  num: (n: number) => string;
}) {
  const entries = Object.entries(stats).filter(
    ([, v]) =>
      (typeof v === "number" && v !== 0) ||
      (Array.isArray(v) && v.some((n) => n !== 0)),
  );
  if (entries.length === 0) return null;
  return (
    <div className="grid grid-cols-1 gap-x-8">
      {entries.map(([k, v]) => (
        <Field
          key={k}
          label={STAT_LABEL[k] ?? k.toUpperCase()}
          value={
            k === "resist" && Array.isArray(v)
              ? v.map((n, i) =>
                  `${dyn(t, `resist.${i}`, `#${i}`)} ${n}`).join(" · ")
              : Array.isArray(v)
                ? v.join(" / ")
                : num(v as number)
          }
        />
      ))}
    </div>
  );
}

// Références liées vers d'autres datasets — chip lien.
function RefChips({
  refs, href, label,
}: {
  refs?: { id: number; name?: string }[] | number[] | string[];
  href: (id: number) => string;
  label?: string;
}) {
  if (!refs?.length) return null;
  const norm = refs.map((r) =>
    typeof r === "number"
      ? { id: r, name: `#${r}` }
      : typeof r === "string"
        ? null
        : { id: r.id, name: r.name ?? `#${r.id}` },
  );
  return (
    <div className="flex flex-wrap gap-1.5">
      {norm.map((r, i) =>
        r === null ? (
          <span
            key={i}
            className="rounded border border-raised px-2 py-0.5 text-xs text-mutedx"
          >
            {(refs as string[])[i]}
          </span>
        ) : (
          <Link
            key={r.id}
            href={href(r.id)}
            className="rounded border border-raised px-2 py-0.5 text-xs text-mutedx hover:border-gold/50 hover:text-gold"
          >
            {r.name}
          </Link>
        ),
      )}
      {label}
    </div>
  );
}

// Proc / effet spécial de l'enchant lié à l'item (W1) — rendu comme
// une stat : libellé « proc » à gauche, texte d'effet à droite.
type ItemEffect = {
  enchantId?: number; name?: string; desc?: string; short?: string;
  slots?: string; event?: string; chance?: number;
};

function EffectRows({ effects, t }: { effects?: ItemEffect[]; t: TFunc }) {
  if (!effects?.length) return null;
  return (
    <>
      {effects.map((e, i) => (
        <div
          key={e.enchantId ?? i}
          className="border-b border-raised/50 py-1.5 text-sm last:border-0"
        >
          <div className="flex justify-between gap-4">
            <span className="text-dim">
              {e.event
                ? dyn(t, `detail.procEvent.${e.event}`,
                    t("detail.proc"))
                : t("detail.proc")}
            </span>
            {e.chance !== undefined && (
              <span className="tabular-nums text-aurora">
                {e.chance}%
              </span>
            )}
          </div>
          {(e.short || e.desc) && (
            <p className="mt-0.5 text-xs leading-snug text-aurora">
              {e.short || e.desc}
            </p>
          )}
          {e.name && (
            <p className="text-[11px] text-dim">{e.name}</p>
          )}
        </div>
      ))}
    </>
  );
}

function ItemBody({ row, t, num }: { row: Row; t: TFunc; num: (n: number) => string }) {
  const crafts: number[] = Array.isArray(row.usedInCrafts)
    ? row.usedInCrafts : [];
  const effects: ItemEffect[] = Array.isArray(row.effects)
    ? row.effects : [];
  return (
    <>
      <div className="mt-4 grid gap-4 lg:grid-cols-[minmax(260px,340px)_1fr]">
        <Panel title={t("detail.stats")}>
          <StatsGrid stats={row.stats ?? {}} t={t} num={num} />
          <EffectRows effects={effects} t={t} />
          {!row.stats && effects.length === 0 && (
            <p className="text-sm text-dim">—</p>
          )}
        </Panel>
        <Panel>
          <Field label={t("detail.category")} value={row.category} />
          <Field
            label={t("detail.quality")}
            value={row.quality
              ? t(`detail.q.${Math.min(row.quality, 5)}`)
              : null}
          />
          <Field label={t("detail.reqLevel")} value={row.reqLevel} />
          {row.moveSpeedBonus ? (
            <Field
              label={t("detail.moveSpeed", { n: row.moveSpeedBonus })}
              value="✦"
            />
          ) : null}
          <Field label={t("detail.equipSlot")} value={row.equipLoc} />
          <Field label={t("detail.maxStack")} value={row.maxStack} />
          <Field label={t("detail.sockets")} value={row.sockets} />
          <Field label={t("detail.durability")} value={row.durability} />
          <Field
            label={t("detail.sellPrice")}
            value={row.sellPrice ? num(row.sellPrice) : null}
          />
        </Panel>
      </div>
      {crafts.length > 0 && (
        <Panel className="mt-4">
          <Link
            href={`/wiki/crafts?f=${encodeURIComponent(`materials.id:${row.id}`)}`}
            className="text-sm text-gold hover:underline"
          >
            {t("detail.usedInCrafts", { n: crafts.length })}
          </Link>
        </Panel>
      )}
      <DropList title={t("detail.contains")} drops={row.contains} />
      <DroppedBy mobs={row.droppedBy} t={t} />
      <Description text={row.desc} t={t} />
    </>
  );
}

// Certificat = buff de classe équipé (WD3) — une entrée par
// certificat, les 2 niveaux (I/II) dans `levels`. Champs : buff,
// classe d'origine, stat altérée, niveau de classe requis.
// PAS de sell price / max stack.
function CertificateBody({ row, t }: { row: Row; t: TFunc }) {
  const levels: {
    itemId: number; name?: string; classLevel: number;
    buff?: { enchantId?: number; name?: string; effect?: string };
  }[] = Array.isArray(row.levels) ? row.levels : [];
  const combos: { with?: string; name?: string; effect?: string }[] =
    Array.isArray(row.combos) ? row.combos : [];
  return (
    <>
      <div className="mt-4 grid gap-4 sm:grid-cols-2">
        <Panel>
          <Field
            label={t("detail.certClass")}
            value={row.classId
              ? (
                <Link
                  href={`/wiki/classes/${row.classId}`}
                  className="text-gold hover:underline"
                >
                  {row.className ?? `#${row.classId}`}
                </Link>
              )
              : row.className}
          />
          <Field label={t("wiki.stat")} value={row.stat} />
          <Field
            label={t("wiki.branch")}
            value={row.color
              ? dyn(t, `wiki.color.${row.color}`, row.color)
              : null}
          />
        </Panel>
        {levels.length > 0 && (
          <Panel title={t("detail.certTiers")}>
            {levels.map((lv) => (
              <div
                key={lv.itemId}
                className="border-b border-raised/50 py-2 last:border-0"
              >
                <div className="flex items-center justify-between gap-2">
                  <Link
                    href={`/wiki/items/${lv.itemId}`}
                    className="text-sm text-gold hover:underline"
                  >
                    {lv.name ?? `#${lv.itemId}`}
                  </Link>
                  <span className="text-xs text-dim tabular-nums">
                    {t("detail.certLevel")} {lv.classLevel}
                  </span>
                </div>
                {lv.buff?.effect && (
                  <p className="mt-0.5 text-xs text-aurora">
                    {lv.buff.effect}
                  </p>
                )}
              </div>
            ))}
          </Panel>
        )}
      </div>
      {combos.length > 0 && (
        <Panel title={t("detail.certCombos")} className="mt-4">
          <ul className="space-y-2">
            {combos.map((c, i) => (
              <li key={i} className="text-sm">
                <span className="text-gold">
                  {t("detail.certComboWith", { name: c.with ?? "?" })}
                </span>
                {c.name && (
                  <span className="ml-2 font-medium">{c.name}</span>
                )}
                {c.effect && (
                  <span className="ml-2 text-xs text-aurora">
                    {c.effect}
                  </span>
                )}
              </li>
            ))}
          </ul>
        </Panel>
      )}
      <Description text={row.desc} t={t} />
    </>
  );
}

function MonsterBody({ row, t, num }: { row: Row; t: TFunc; num: (n: number) => string }) {
  // Spawns réels scène (WD7) : [{map:"S648",name?,x,y,count,
  // respawnSec}] — dédupliqués par code de map ; null si le mob
  // n'apparaît que par event/script (world boss).
  const spawns: { map: string; name?: string; count?: number }[] =
    Array.isArray(row.maps) ? row.maps : [];
  const mapByName = new Map<string, { count: number }>();
  for (const s of spawns) {
    const cur = mapByName.get(s.map) ?? { count: 0 };
    cur.count += s.count ?? 0;
    mapByName.set(s.map, cur);
  }
  const skills: { id: number; name?: string }[] =
    Array.isArray(row.skills) && row.skills.length > 0
      ? row.skills
      : (row.refIds ?? []).map((sid: number) => ({ id: sid }));
  const bp: { classGroup?: number; className?: string; id: number;
    name?: string; icon?: string; qty: number; chance: number }[] =
    Array.isArray(row.bpDrops) ? row.bpDrops : [];
  return (
    <>
      {row.title && (
        <p className="mt-2 text-sm text-crystal">{row.title}</p>
      )}
      <div className="mt-4 grid gap-4 lg:grid-cols-2">
        <Panel>
          <Field label={t("detail.level")} value={row.level} />
          <Field
            label={t("detail.type")}
            value={row.type
              ? dyn(t, `wiki.mobType.${row.type}`, `Type ${row.type}`)
              : null}
          />
          <Field label={t("detail.hp")} value={row.hp ? num(row.hp) : null} />
          <Field label={t("detail.mp")} value={row.mp ? num(row.mp) : null} />
          <Field
            label={t("detail.atkPower")}
            value={row.atkPower ? num(row.atkPower) : null}
          />
          <Field
            label={t("detail.pDef")}
            value={row.pDef ? num(row.pDef) : null}
          />
          <Field
            label={t("detail.expReward")}
            value={row.exp ? num(row.exp) : null}
          />
          <Field label={t("detail.classExp")} value={row.classExp} />
          <Field
            label={t("detail.aggro")}
            value={row.aggro ? `${row.aggro} u` : null}
          />
          <Field label={t("detail.model")} value={row.model} />
        </Panel>
        <div className="space-y-4">
          {mapByName.size > 0 && (
            <Panel title={t("detail.spawnMap")}>
              <div className="flex flex-wrap gap-1.5">
                {[...mapByName.entries()].map(([code, info]) => (
                  <Link
                    key={code}
                    href={`/wiki/maps?f=${encodeURIComponent(`mapCode:${code}`)}`}
                    className="rounded border border-raised px-2 py-0.5 text-xs text-mutedx hover:border-gold/50 hover:text-gold"
                  >
                    {code}
                    {info.count > 0 && (
                      <span className="ml-1 text-dim">×{info.count}</span>
                    )}
                  </Link>
                ))}
              </div>
            </Panel>
          )}
          {Array.isArray(row.resist) && row.resist.some(
            (n: number) => n !== 0) && (
            <Panel title={t("detail.resists")}>
              <div className="grid grid-cols-3 gap-x-6">
                {row.resist.map((n: number, i: number) => (
                  <Field
                    key={i}
                    label={dyn(t, `resist.${i}`, `#${i}`)}
                    value={n !== 0 ? n : null}
                  />
                ))}
              </div>
            </Panel>
          )}
        </div>
      </div>
      {skills.length > 0 && (
        <Panel title={t("detail.skillsUsed")} className="mt-4">
          <RefChips refs={skills} href={(id) => `/wiki/spells/${id}`} />
        </Panel>
      )}
      <DropList title={t("detail.drops")} drops={row.drops} />
      {bp.length > 0 && (
        <Panel title={t("detail.bpDrops")} className="mt-4">
          <ul className="space-y-2">
            {bp.map((d, i) => (
              <li key={i} className="flex items-center gap-2.5">
                <GameIcon code={d.icon} name={d.name} size={24} />
                <span className="min-w-0 flex-1 truncate text-sm">
                  <Link
                    href={`/wiki/items/${d.id}`}
                    className="hover:text-gold"
                  >
                    {d.name ?? `#${d.id}`}
                  </Link>
                </span>
                <span className="shrink-0 text-xs text-dim">
                  {d.className ??
                    t("detail.classGroup", { n: d.classGroup ?? 0 })}
                  {d.qty > 1 ? ` · ×${d.qty}` : ""}
                  {d.chance < 100 ? ` · ${d.chance}%` : ""}
                </span>
              </li>
            ))}
          </ul>
        </Panel>
      )}
    </>
  );
}

function MissionBody({ row, t, num }: { row: Row; t: TFunc; num: (n: number) => string }) {
  return (
    <>
      <Panel className="mt-4">
        <Field label={t("detail.type")} value={row.type} />
        <Field label={t("detail.zone")} value={row.map} />
        <Field
          label={t("detail.levelRange")}
          value={row.minLevel || row.maxLevel
            ? `${row.minLevel || "?"} – ${row.maxLevel || "∞"}` : null}
        />
        {row.daily && <Field label={t("detail.resets")} value={t("detail.daily")} />}
        <Field label={t("detail.completions")} value={row.completeLimit} />
      </Panel>
      {row.rewards && (
        <Panel title={t("detail.rewards")} className="mt-4">
          <Field label={t("detail.gold")} value={row.rewards.gold ? num(row.rewards.gold) : null} />
          <Field label={t("detail.exp")} value={row.rewards.exp ? num(row.rewards.exp) : null} />
          {row.rewards.reputation?.map((r: string, i: number) => (
            <Field key={i} label={t("detail.reputation")} value={r} />
          ))}
          {row.rewards.items?.map(
            (it: { id: number; qty: number; name?: string }) => (
              <Field
                key={it.id}
                label={t("detail.item")}
                value={
                  <Link
                    href={`/wiki/items/${it.id}`}
                    className="text-gold hover:underline"
                  >
                    {it.name ?? `#${it.id}`} ×{it.qty}
                  </Link>
                }
              />
            ),
          )}
          {row.rewards.chooseItems?.map(
            (it: { id: number; qty: number; name?: string }) => (
              <Field
                key={it.id}
                label={t("detail.choiceOf")}
                value={
                  <Link
                    href={`/wiki/items/${it.id}`}
                    className="text-gold hover:underline"
                  >
                    {it.name ?? `#${it.id}`} ×{it.qty}
                  </Link>
                }
              />
            ),
          )}
        </Panel>
      )}
      {row.conditions?.length > 0 && (
        <Panel title={t("detail.conditions")} className="mt-4">
          <ul className="space-y-1 font-mono text-xs text-dim">
            {row.conditions.map((c: string) => <li key={c}>{c}</li>)}
          </ul>
        </Panel>
      )}
      <Description text={row.desc} t={t} />
    </>
  );
}

function SpellBody({ row, t }: { row: Row; t: TFunc }) {
  return (
    <>
      <Panel className="mt-4">
        <Field label={t("detail.family")} value={row.family} />
        <Field label={t("detail.reqLevel")} value={row.reqLevel} />
        <Field
          label={t("detail.castTime")}
          value={row.castDs ? `${(row.castDs / 10).toFixed(1)} s` : null}
        />
        <Field
          label={t("detail.cooldown")}
          value={row.cooldownDs ? `${(row.cooldownDs / 10).toFixed(1)} s` : null}
        />
        <Field label={t("detail.mpCost")} value={row.mpCost} />
        <Field label={t("detail.power")} value={row.power} />
        <Field label={t("detail.healPower")} value={row.healPower} />
      </Panel>
      <Description text={row.desc} t={t} />
    </>
  );
}

// Classe (WD4) — piste de licences en tête (certificats par palier
// de 10 niv. avec leur buff), puis 3 sections : skills actifs /
// passifs de classe / bonus de rang (stats passives) ; requiredClasses
// et statPct en contexte. « knowledge » brut retiré (inexplicable).
function SpellChips({
  list,
}: {
  list: { id: number; name?: string; desc?: string }[];
}) {
  return (
    <div className="flex flex-wrap gap-1.5">
      {list.map((s) => (
        <Link
          key={s.id}
          href={`/wiki/spells/${s.id}`}
          title={s.desc ?? undefined}
          className="rounded border border-raised px-2 py-0.5 text-xs text-mutedx hover:border-gold/50 hover:text-gold"
        >
          {s.name ?? `#${s.id}`}
        </Link>
      ))}
    </div>
  );
}

const BRANCH_COLOR: Record<string, string> = {
  yellow: "text-gold", orange: "text-[#e8935a]", green: "text-aurora",
  violet: "text-crystal", blue: "text-[#6ba8f0]",
};

function ClassBody({ row, t }: { row: Row; t: TFunc }) {
  const licenses: {
    classLevel: number; itemId: number; name?: string; icon?: string;
    buff?: { enchantId?: number; name?: string; effect?: string };
  }[] = Array.isArray(row.licenses) ? row.licenses : [];
  const skills: { id: number; name?: string; desc?: string }[] =
    row.skills ?? [];
  const passives: { id: number; name?: string; desc?: string }[] =
    row.passives ?? [];
  const rankBuffs: { rank: number; classLevel: number; reqLevel: number;
    enchantId?: number; name?: string; effect?: string }[] =
    Array.isArray(row.rankBuffs) ? row.rankBuffs : [];
  const statPct: Record<string, number> | undefined =
    row.statPct && typeof row.statPct === "object" ? row.statPct : undefined;
  const required: { id: number; name?: string; level: number }[] =
    Array.isArray(row.requiredClasses) ? row.requiredClasses : [];
  return (
    <>
      <Panel className="mt-4">
        <Field
          label={t("detail.tier")}
          value={row.awakened ? t("detail.awakened") : t("detail.base")}
        />
        <Field
          label={t("wiki.branch")}
          value={row.color
            ? <span className={BRANCH_COLOR[row.color] ?? "text-ink"}>
                {dyn(t, `wiki.color.${row.color}`, row.color)}
              </span>
            : null}
        />
        <Field label={t("detail.trait")} value={row.trait} />
        <Field label={t("detail.charLevel")} value={row.charLevel} />
        {row.parentClassId != null && (
          <Field
            label={t("detail.parentClass")}
            value={
              <Link
                href={`/wiki/classes/${row.parentClassId}`}
                className="text-gold hover:underline"
              >
                #{row.parentClassId}
              </Link>
            }
          />
        )}
      </Panel>
      {required.length > 0 && (
        <Panel title={t("detail.requiredClasses")} className="mt-4">
          <div className="flex flex-wrap gap-1.5">
            {required.map((r) => (
              <Link
                key={r.id}
                href={`/wiki/classes/${r.id}`}
                className="rounded border border-raised px-2 py-0.5 text-xs text-mutedx hover:border-gold/50 hover:text-gold"
              >
                {r.name ?? `#${r.id}`} · Lv {r.level}
              </Link>
            ))}
          </div>
        </Panel>
      )}
      {row.requirement && (
        <Panel title={t("detail.requirement")} className="mt-4">
          <p className="whitespace-pre-wrap text-sm text-ink/85">
            {row.requirement}
          </p>
        </Panel>
      )}
      {licenses.length > 0 && (
        <Panel title={t("detail.licenses")} className="mt-4">
          <ol className="grid gap-2 sm:grid-cols-2">
            {licenses.map((l) => (
              <li key={l.itemId} className="panel-raised flex items-center gap-2.5 p-2">
                <GameIcon code={l.icon} name={l.name} size={28} />
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm">
                    {l.name ?? `#${l.itemId}`}
                  </span>
                  {l.buff?.effect && (
                    <span className="block truncate text-[11px] text-aurora">
                      {l.buff.effect}
                    </span>
                  )}
                </span>
                <span className="shrink-0 text-xs text-dim tabular-nums">
                  Lv {l.classLevel}
                </span>
              </li>
            ))}
          </ol>
        </Panel>
      )}
      {skills.length > 0 && (
        <Panel title={t("detail.classSkills")} className="mt-4">
          <SpellChips list={skills} />
        </Panel>
      )}
      {passives.length > 0 && (
        <Panel title={t("detail.classPassives")} className="mt-4">
          <SpellChips list={passives} />
        </Panel>
      )}
      {rankBuffs.length > 0 && (
        <Panel title={t("detail.rankBuffs")} className="mt-4">
          <ul className="grid gap-1 sm:grid-cols-2">
            {rankBuffs.map((b) => (
              <li
                key={`${b.rank}-${b.classLevel}`}
                className="flex items-baseline justify-between gap-3 border-b border-raised/40 py-1 text-xs last:border-0"
              >
                <span className="min-w-0 truncate">
                  {b.name ?? `#${b.enchantId}`}
                </span>
                <span className="shrink-0 text-dim tabular-nums">
                  Lv {b.classLevel}
                </span>
              </li>
            ))}
          </ul>
        </Panel>
      )}
      {statPct && Object.keys(statPct).length > 0 && (
        <Panel title={t("detail.statPct")} className="mt-4">
          <div className="grid grid-cols-3 gap-x-6 sm:grid-cols-5">
            {Object.entries(statPct).map(([k, v]) => (
              <Field
                key={k}
                label={k.replace("Pct", "").toUpperCase()}
                value={`${v}%`}
              />
            ))}
          </div>
        </Panel>
      )}
      <Description text={row.short ?? row.desc} t={t} />
    </>
  );
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function DetailBody({
  kind, row, t, num,
}: {
  kind: string; row: Row; t: TFunc; num: (n: number) => string;
}) {
  switch (kind) {
    case "items":
      return <ItemBody row={row} t={t} num={num} />;
    case "certificates":
      return <CertificateBody row={row} t={t} />;
    case "monsters":
      return <MonsterBody row={row} t={t} num={num} />;
    case "missions":
      return <MissionBody row={row} t={t} num={num} />;
    case "spells":
      return <SpellBody row={row} t={t} />;
    case "classes":
      return <ClassBody row={row} t={t} />;
    case "achievements":
      return (
        <>
          <Panel className="mt-4">
            <Field label={t("detail.achPoints")} value={row.points} />
            <Field label={t("detail.objective")} value={row.target} />
            <Field label={t("detail.count")} value={row.count} />
          </Panel>
          <Description text={row.desc} t={t} />
        </>
      );
    case "crafts":
      return (
        <Panel title={t("detail.materials")} className="mt-4">
          <ul className="grid gap-2 sm:grid-cols-2">
            {row.materials?.map(
              (m: { id: number; name?: string; icon?: string }) => (
                <li key={m.id}>
                  <Link
                    href={`/wiki/items/${m.id}`}
                    className="panel-raised flex items-center gap-2.5 p-2 hover:border-gold/50"
                  >
                    <GameIcon code={m.icon} name={m.name} size={28} />
                    <span className="truncate text-sm">
                      {m.name ?? `#${m.id}`}
                    </span>
                  </Link>
                </li>
              ),
            )}
          </ul>
        </Panel>
      );
    case "titles":
      return (
        <>
          <Panel className="mt-4">
            <Field label={t("detail.category")} value={row.category} />
            <Field label={t("detail.bonus")} value={row.bonus} />
          </Panel>
          <Description text={row.desc} t={t} />
        </>
      );
    case "npcs":
      return (
        <>
          <Panel className="mt-4">
            <Field label={t("detail.code")} value={row.code} />
            <Field label={t("detail.role")} value={row.role} />
          </Panel>
          <Description text={row.info} t={t} />
        </>
      );
    case "maps":
      return (
        <>
          <Panel className="mt-4">
            <Field
              label={t("detail.type")}
              value={row.type === 3
                ? t("detail.town")
                : t("detail.zoneN", { n: row.type })}
            />
          </Panel>
          {typeof row.mapCode === "string" && row.mapCode && (
            <Panel title={t("detail.mapPreview")} className="mt-4">
              {/* Vraie carte du monde (UI/map/Sxxx.dds → webp). */}
              <MapArt
                code={row.mapCode}
                alt={t("detail.mapAlt", { name: row.name ?? row.mapCode })}
                className="mt-1"
              />
            </Panel>
          )}
          <Description text={row.desc} t={t} />
        </>
      );
    case "talents":
      return (
        <Panel
          title={t("detail.talentSlots", { n: row.slots?.length ?? 0 })}
          className="mt-4"
        >
          <ul className="space-y-1 text-sm">
            {row.slots?.map(
              (s: {
                effectId: number; name?: string; maxPts: number;
                reqEffectId: number; reqPts: number; levelReq: number;
              }) => (
                <li
                  key={s.effectId}
                  className="flex justify-between border-b border-raised/50 py-1 last:border-0"
                >
                  <span>{s.name ?? `Effect ${s.effectId}`}</span>
                  <span className="text-xs text-dim">
                    {t("detail.pts", { n: s.maxPts })}
                    {s.levelReq
                      ? ` · ${t("detail.classLvReq", { n: s.levelReq })}`
                      : ""}
                  </span>
                </li>
              ),
            )}
          </ul>
        </Panel>
      );
    default:
      return null;
  }
}

export default async function WikiDetailPage({
  params,
}: {
  params: Promise<{ kind: string; id: string }>;
}) {
  const { kind, id } = await params;
  const { t } = await getT();
  const locale = await getLocale();
  const num = localeFmt(locale).num;
  const kindLabel = DATASETS.some((d) => d.id === kind)
    ? t(`ds.${kind}`)
    : null;
  if (!kindLabel) notFound();

  let row: Row | null = null;
  let unreachable = false;
  if (kind === "certificates") {
    // Certificats : id chaîne « classId:Name » — pas de route détail
    // int côté backend ; on résout via la liste (60 entrées).
    const rawId = decodeURIComponent(id);
    try {
      const page = await serverGet<{ items: Row[] }>(
        "/api/wiki/certificates?size=100", 60);
      row = page.items.find((r) => String(r.id) === rawId) ?? null;
    } catch (ex) {
      if (!(ex instanceof ApiError && ex.status === 404)) {
        unreachable = true;
      }
    }
  } else {
    const idNum = Number(id);
    if (!Number.isInteger(idNum)) notFound();
    try {
      row = await serverGet<Record<string, unknown>>(
        `/api/wiki/${kind}/${idNum}`, 60);
    } catch (ex) {
      // 404 = l'entrée n'existe vraiment pas → notFound. Toute autre
      // erreur (API coupée, 5xx, réseau) → panneau d'erreur honnête,
      // jamais le message trompeur « cette entrée n'existe pas ».
      if (ex instanceof ApiError && ex.status === 404) {
        row = null;
      } else {
        unreachable = true;
      }
    }
  }
  if (unreachable) {
    return (
      <div className="mx-auto mt-16 max-w-md text-center">
        <h1 className="font-display text-2xl font-bold text-ink">
          {t("common.unreachable")}
        </h1>
        <p className="mt-3 text-sm text-dim">
          {t("detail.unreachableSub")}
        </p>
        <Link
          href={`/wiki?kind=${kind}`}
          className="mt-6 inline-block rounded-md border border-raised px-4 py-2 text-sm text-gold hover:border-gold/60"
        >
          {t("common.backToList")}
        </Link>
      </div>
    );
  }
  if (row === null) notFound();

  const name = row.name ?? `#${row.id ?? decodeURIComponent(id)}`;
  const q = typeof row.quality === "number" ? row.quality : 0;
  const qCls = q >= 2 ? `q${Math.min(q, 5)}` : "text-ink";
  return (
    <div className="mx-auto max-w-4xl">
      <nav className="mb-4 text-xs text-dim">
        <Link href="/wiki" className="hover:text-gold">
          {t("detail.wikiCrumb")}
        </Link>
        {" / "}
        <Link href={`/wiki?kind=${kind}`} className="hover:text-gold">
          {kindLabel}
        </Link>
        {" / "}
        <span className="text-ink">{name}</span>
      </nav>
      <header className="panel flex items-center gap-4 px-5 py-4">
        <EntityIcon
          kind={kind} code={row.icon} name={row.name} rec={row} size={56}
        />
        <div className="min-w-0">
          <h1 className={`truncate font-display text-2xl font-bold ${qCls}`}>
            {name}
          </h1>
          <p className="text-xs text-dim">
            {kindLabel} · #{row.id ?? row.classId ?? decodeURIComponent(id)}
            {q >= 2 && ` · ${t(`detail.q.${Math.min(q, 5)}`)}`}
            {row.untranslated && ` · ${t("detail.untranslatedFlag")}`}
          </p>
        </div>
      </header>
      <DetailBody kind={kind} row={row} t={t} num={num} />
    </div>
  );
}
