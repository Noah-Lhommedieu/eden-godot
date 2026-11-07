// Client API — même origine côté navigateur (/api → rewrite Next
// vers le backend), URL interne côté serveur (RSC).
export const API_BASE =
  typeof window === "undefined"
    ? (process.env.API_INTERNAL_URL ?? "http://localhost:5154")
    : "";

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const r = await fetch(`${API_BASE}${path}`, {
    cache: "no-store",
    ...init,
  });
  if (!r.ok) {
    let msg = `Error ${r.status}`;
    try {
      const j = await r.json();
      if (j?.error) msg = j.error;
    } catch { /* ignore */ }
    throw new ApiError(r.status, msg);
  }
  return r.json() as Promise<T>;
}

/// Fetch côté serveur avec revalidation (datasets quasi statiques).
export async function serverGet<T>(
  path: string,
  revalidate = 300,
): Promise<T> {
  const r = await fetch(`${API_BASE}${path}`, {
    next: { revalidate },
  } as RequestInit);
  if (!r.ok) throw new ApiError(r.status, `Error ${r.status}`);
  return r.json() as Promise<T>;
}

// --- Auth (JWT en localStorage) ---
const TOKEN_KEY = "ll_token";
const ROLE_KEY = "ll_role";
const NAME_KEY = "ll_name";

export const getToken = () =>
  typeof window !== "undefined" ? localStorage.getItem(TOKEN_KEY) : null;
export const getRole = () =>
  typeof window !== "undefined" ? localStorage.getItem(ROLE_KEY) : null;
export const getName = () =>
  typeof window !== "undefined" ? localStorage.getItem(NAME_KEY) : null;

export function saveSession(token: string, role: string, name: string) {
  localStorage.setItem(TOKEN_KEY, token);
  localStorage.setItem(ROLE_KEY, role);
  localStorage.setItem(NAME_KEY, name);
}
export function clearSession() {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(ROLE_KEY);
  localStorage.removeItem(NAME_KEY);
}

const headers = (token?: string | null): Record<string, string> =>
  token ? { Authorization: `Bearer ${token}` } : {};

export const apiGet = <T,>(path: string, token?: string | null) =>
  request<T>(path, { headers: headers(token) });

export const apiPost = <T,>(
  path: string,
  body: unknown,
  token?: string | null,
) =>
  request<T>(path, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...headers(token) },
    body: JSON.stringify(body),
  });

export const apiPut = <T,>(
  path: string,
  body: unknown,
  token?: string | null,
) =>
  request<T>(path, {
    method: "PUT",
    headers: { "Content-Type": "application/json", ...headers(token) },
    body: JSON.stringify(body),
  });

export const apiDelete = <T,>(path: string, token?: string | null) =>
  request<T>(path, { method: "DELETE", headers: headers(token) });

// --- Types API ---
export interface LoginRes { token: string; accountId: number; role: string }
export interface CharacterRow {
  id: number; name: string; lv: number; classId: number; exp: number;
}
export interface RankRow {
  rank: number; name: string; value: number; classId: number; level: number;
}
export interface DatasetMeta { id: string; count: number; untranslated: number }
export interface Paged<T> { total: number; page: number; size: number; items: T[] }

// Lignes wiki — schémas lâches (champs optionnels par dataset).
export interface WikiItemEffect {
  enchantId?: number; name?: string; desc?: string; short?: string;
  slots?: string; event?: string; chance?: number;
}
export interface WikiItem {
  id: number; icon?: string; name?: string; untranslated?: boolean;
  kind?: number; category?: string; reqLevel?: number;
  sellPrice?: number; maxStack?: number; quality?: number;
  equipLoc?: number; maxLevel?: number; useEnchant?: number;
  sockets?: number; durability?: number; desc?: string;
  mount?: boolean; moveSpeedBonus?: number;
  usedInCrafts?: number[];
  effects?: WikiItemEffect[];
  stats?: Record<string, number | number[]>;
}
export interface WikiMonster {
  id: number; name?: string; title?: string; level?: number;
  type?: number; hp?: number; mp?: number; model?: string;
  icon?: string; // portrait famille du modèle (M### — « succès »)
  exp?: number; classExp?: number; atkPower?: number; pDef?: number;
  aggro?: number; resist?: number[]; refIds?: number[];
  skills?: { id: number; name?: string }[];
  maps?: { map: string; name?: string; x?: number; y?: number;
    count?: number; respawnSec?: number }[];
  drops?: { id: number; name?: string; icon?: string; qty: number;
    chance: number }[];
  bpDrops?: { classGroup?: number; className?: string; id: number;
    name?: string; icon?: string; qty: number; chance: number }[];
  untranslated?: boolean;
}
export interface WikiMission {
  id: number; name?: string; map?: string; minLevel?: number;
  maxLevel?: number; type?: string; desc?: string; daily?: boolean;
  completeLimit?: number; conditions?: string[];
  rewards?: {
    gold: number; exp: number;
    items: { id: number; qty: number; name?: string }[];
    chooseItems: { id: number; qty: number; name?: string }[];
    reputation: string[]; buff: string[]; other: string[];
  };
  untranslated?: boolean;
}
export interface WikiSpell {
  id: number; icon?: string; name?: string; family?: number;
  type?: number; reqLevel?: number; castDs?: number; cooldownDs?: number;
  mpCost?: number; power?: number; healPower?: number;
  desc?: string; category?: string; untranslated?: boolean;
}
export interface WikiClass {
  id: number; icon?: string; name?: string; short?: string;
  trait?: string; desc?: string; requirement?: string;
  awakened?: boolean; branch?: string; color?: string;
  charLevel?: number | null; parentClassId?: number | null;
  requiredClasses?: { id: number; name?: string; level: number }[] | null;
  statPct?: Record<string, number>;
  licenses?: { classLevel: number; itemId: number; name?: string;
    icon?: string; buff?: { enchantId?: number; name?: string;
      effect?: string } }[];
  certificates?: { classLevel: number; itemId: number }[];
  skills?: { id: number; name?: string; desc?: string }[];
  passives?: { id: number; name?: string; desc?: string }[];
  rankBuffs?: { rank: number; classLevel: number; reqLevel: number;
    enchantId?: number; name?: string; effect?: string }[];
  spells?: { id: number; name?: string }[];
  knowledge?: string[]; untranslated?: boolean;
}
export interface WikiCertificate {
  id: string; icon?: string; name?: string; classId?: number;
  className?: string; color?: string; stat?: string;
  combos?: { with?: string; name?: string; effect?: string }[];
  levels?: { itemId: number; name?: string; classLevel: number;
    buff?: { enchantId?: number; name?: string; effect?: string } }[];
  untranslated?: boolean;
}
export interface WikiAchievement {
  id: number; icon?: string; name?: string; category?: number;
  points?: number; target?: number; count?: number; desc?: string;
  untranslated?: boolean;
}
export interface WikiCraft {
  id: number; name?: string;
  materials?: { id: number; name?: string; icon?: string }[];
  untranslated?: boolean;
}
export interface WikiTitle {
  id: number; name?: string; category?: string; bonus?: string;
  desc?: string; untranslated?: boolean;
}
export interface WikiNpc {
  id: number; code?: string; name?: string; info?: string;
  icon?: string; // portrait famille du modèle (M### via NPCList)
  untranslated?: boolean;
}
export interface WikiMap {
  id: number; name?: string; type?: number; desc?: string;
  untranslated?: boolean;
}
export type WikiRow =
  | WikiItem | WikiMonster | WikiMission | WikiSpell | WikiClass
  | WikiAchievement | WikiCraft | WikiTitle | WikiNpc | WikiMap
  | WikiCertificate;

// Talents — schéma WD5 : 61 classes × 3 arbres ; règles natives AA
// via GET /api/talents/rules (pointsPerClassLevel, cap 100, coûts).
export interface TalentSlot {
  pos: number; effectId: number; name?: string; desc?: string;
  maxPts: number; preEffect?: number | null; prePoints?: number | null;
  tier?: number; reqPoints?: number | null; icon?: string | null;
}
export interface TalentSubTree {
  tree: number; slots: TalentSlot[];
}
export interface TalentTree {
  classId: number; className?: string; trees: TalentSubTree[];
}
export interface TalentRules {
  pointsPerClassLevel: number; maxCharacterLevel: number;
  maxAaPoints: number; costPerRank: number[]; costTableTotal: number[];
}
export interface TalentClassEntry {
  classId: number; className?: string; trees: number;
  awakened?: boolean; icon?: string;
}
export interface ServerStatus { online: number; total: number; now: string }
export interface NewsRow { id: number; title: string; body: string; created_at: string }
export interface Perk {
  code: string; name: string; description: string;
  requiredLevel: number; value: number; valueKind: string;
}
export interface LimitlessState {
  charId: number; name?: string; exp: number; level: number;
  expToNext?: number; perks: Perk[];
}
export interface LevelCost { level: number; exp_req: number }
export interface GuideRow {
  id: number; slug: string; title: string; category: string;
  body: string; sortOrder: number; published: boolean;
  author: string; createdAt: string; updatedAt: string;
}
export interface GuideUpsert {
  title: string; category: string; body: string;
  sortOrder: number; published: boolean; slug?: string;
}
