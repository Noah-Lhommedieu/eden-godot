/// Datasets du wiki (ordre du rail) — clés = fichiers JSON produits
/// par tools/export_web_data.py. `labelKey` = clé i18n ds.<id>.
export const DATASETS: { id: string; labelKey: string }[] = [
  { id: "items", labelKey: "ds.items" },
  { id: "certificates", labelKey: "ds.certificates" },
  { id: "monsters", labelKey: "ds.monsters" },
  { id: "missions", labelKey: "ds.missions" },
  { id: "spells", labelKey: "ds.spells" },
  { id: "classes", labelKey: "ds.classes" },
  { id: "achievements", labelKey: "ds.achievements" },
  { id: "crafts", labelKey: "ds.crafts" },
  { id: "titles", labelKey: "ds.titles" },
  { id: "npcs", labelKey: "ds.npcs" },
  { id: "maps", labelKey: "ds.maps" },
];

export const KIND_IDS = new Set(DATASETS.map((d) => d.id));

/// Filtres serveur (`f=champ:valeur`, matching exact sur scalaires —
/// cf. GameDataStore.Query). Un seul filtre actif à la fois tant que
/// le backend ne prend qu'un `f` ; les options couvrent les cas
/// courants, et un champ libre « champ:valeur » permet le reste.
/// `groupKey` = clé i18n du groupe d'options ; `labelKey` = clé de
/// chaque valeur.
export type FilterOption = { f: string; labelKey: string };
export type FilterGroup = { groupKey: string; options: FilterOption[] };

const itemCats = [
  "Weapon", "Armor", "Consumable", "Gem", "Skill book",
  "Certificate", "Special", "Bag", "Mount", "Item",
];
const qualities = [1, 2, 3, 4, 5];
const classColors = ["yellow", "orange", "green", "violet", "blue"];
const questTypes = [
  "Main", "Quest", "Daily", "Repeatable", "Adventure", "Special",
];
const titleCats = [
  "General Title", "Honor Title", "Festival Title",
  "Book Title", "Prestige Title",
];

export const KIND_FILTERS: Record<string, FilterGroup[]> = {
  items: [
    {
      groupKey: "detail.category",
      options: itemCats.map((c) => ({
        f: `category:${c}`, labelKey: `wiki.cat.${c}`,
      })),
    },
    {
      groupKey: "detail.quality",
      options: qualities.map((q) => ({
        f: `quality:${q}`, labelKey: `detail.q.${q}`,
      })),
    },
    {
      groupKey: "detail.reqLevel",
      options: [
        { f: "reqLevel<30", labelKey: "common.level" },
        { f: "reqLevel>=30", labelKey: "common.level" },
        { f: "reqLevel>=60", labelKey: "common.level" },
        { f: "reqLevel>=90", labelKey: "common.level" },
      ].map((o, i) => ({
        ...o,
        labelKey: `wiki.lvBand.${i}`,
      })),
    },
  ],
  certificates: [
    {
      groupKey: "wiki.branch",
      options: classColors.map((c) => ({
        f: `color:${c}`, labelKey: `wiki.color.${c}`,
      })),
    },
  ],
  monsters: [
    {
      groupKey: "detail.type",
      options: [1, 2, 3, 4, 5].map((n) => ({
        f: `type:${n}`, labelKey: `wiki.mobType.${n}`,
      })),
    },
    {
      groupKey: "detail.level",
      options: [
        { f: "level<30", labelKey: "wiki.lvBand.0" },
        { f: "level>=30", labelKey: "wiki.lvBand.1" },
        { f: "level>=60", labelKey: "wiki.lvBand.2" },
        { f: "level>=90", labelKey: "wiki.lvBand.3" },
      ],
    },
  ],
  missions: [
    {
      groupKey: "detail.type",
      options: questTypes.map((m) => ({
        f: `type:${m}`, labelKey: `wiki.questType.${m}`,
      })),
    },
  ],
  classes: [
    {
      groupKey: "detail.tier",
      options: [
        { f: "awakened:false", labelKey: "wiki.awakened.false" },
        { f: "awakened:true", labelKey: "wiki.awakened.true" },
      ],
    },
    {
      groupKey: "wiki.branch",
      options: classColors.map((c) => ({
        f: `color:${c}`, labelKey: `wiki.color.${c}`,
      })),
    },
  ],
  titles: [
    {
      groupKey: "detail.category",
      options: titleCats.map((c) => ({
        f: `category:${c}`, labelKey: `wiki.titleCat.${c}`,
      })),
    },
  ],
};
