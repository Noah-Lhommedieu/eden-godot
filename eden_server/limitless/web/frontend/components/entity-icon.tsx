"use client";

// Vignette d'entité wiki — trois niveaux de repli :
// 1. code icône du jeu (public/icons/<CODE>.webp) → GameIcon ;
// 2. image dédiée au dataset (cartes : /art/maps/<CODE>.webp) ;
// 3. médaillon glyphe coloré par type/catégorie — jamais une
//    simple lettre.
import { useState } from "react";
import {
  Award, Bird, Bone, BookOpen, Bug, Cat, Coins, Crown, Eye, Flag,
  Fish, Gem, Ghost, Hammer, KeyRound, Map as MapIcon, Medal, Package,
  Rabbit, Rat, ScrollText, Shield, ShieldCheck, Skull, Sparkles,
  Squirrel, Swords, Trophy, Users, Wrench, Zap,
} from "lucide-react";
import { GameIcon } from "@/components/icon-img";
import { cn } from "@/lib/utils";

type Rec = Record<string, unknown>;

const GLYPH_CLS =
  "game-icon inline-flex items-center justify-center";

// Médaillon glyphe — cadre + teinte par catégorie, symbole lucide
// réel (pas d'initiale).
function Glyph({
  children, tint, size, className, label,
}: {
  children: React.ReactNode;
  tint: string;
  size: number;
  className?: string;
  label?: string;
}) {
  return (
    <span
      role={label ? "img" : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
      className={cn(GLYPH_CLS, tint, className)}
      style={{ width: size, height: size, fontSize: size * 0.5 }}
    >
      {children}
    </span>
  );
}

// Vignettes riches par type — le jeu n'a pas de portraits
// (modèles 3D) : glyphe expressif par type + variation
// déterministe par id pour éviter la grille uniforme.
const MONSTER_ICONS = [
  <Rabbit key="r" size="55%" strokeWidth={1.8} />,
  <Bug key="bug" size="55%" strokeWidth={1.8} />,
  <Bird key="bird" size="55%" strokeWidth={1.8} />,
  <Cat key="cat" size="55%" strokeWidth={1.8} />,
  <Rat key="rat" size="55%" strokeWidth={1.8} />,
  <Fish key="f" size="55%" strokeWidth={1.8} />,
  <Squirrel key="sq" size="55%" strokeWidth={1.8} />,
  <Eye key="eye" size="55%" strokeWidth={1.8} />,
];
const MONSTER_GLYPH: Record<number, { icons: React.ReactNode[]; tint: string }> = {
  1: { icons: MONSTER_ICONS, tint: "border-raised bg-night text-mutedx" },
  2: { icons: [<Shield key="s" size="55%" strokeWidth={1.8} />,
               <ShieldCheck key="sc" size="55%" strokeWidth={1.8} />,
               <Zap key="z" size="55%" strokeWidth={1.8} />],
       tint: "border-[#6ba8f0]/60 bg-[#6ba8f0]/10 text-[#6ba8f0]" },
  3: { icons: [<Crown key="c" size="55%" strokeWidth={1.8} />,
               <Bone key="b" size="55%" strokeWidth={1.8} />,
               <Skull key="sk" size="55%" strokeWidth={1.8} />],
       tint: "border-gold/60 bg-gold/10 text-gold" },
  4: { icons: [<Ghost key="g" size="55%" strokeWidth={1.8} />,
               <Skull key="sk" size="55%" strokeWidth={1.8} />],
       tint: "border-crystal/60 bg-crystal/10 text-crystal" },
  5: { icons: [<Ghost key="g" size="55%" strokeWidth={1.8} />,
               <Skull key="sk" size="55%" strokeWidth={1.8} />],
       tint: "border-ember/60 bg-ember/10 text-ember" },
};

// PNJ : glyphe selon le rôle annoncé dans t_npc (Shop, Quest,
// Teleport, Warehouse…), repli « personnage ».
const NPC_GLYPH: [RegExp, React.ReactNode][] = [
  [/shop|merchant|store|vendor|grocer|seller/i,
    <Coins key="c" size="55%" strokeWidth={1.8} />],
  [/quest|mission|daily|adventure/i,
    <ScrollText key="q" size="55%" strokeWidth={1.8} />],
  [/teleport|portal|transport|warp/i,
    <KeyRound key="t" size="55%" strokeWidth={1.8} />],
  [/warehouse|storage|bank|deposit/i,
    <Package key="w" size="55%" strokeWidth={1.8} />],
  [/craft|smith|forge|workshop|repair/i,
    <Wrench key="cr" size="55%" strokeWidth={1.8} />],
  [/guard|captain|knight|soldier/i,
    <Shield key="g" size="55%" strokeWidth={1.8} />],
  [/guild|family|leader|master|chief|elder|mayor|king/i,
    <Crown key="k" size="55%" strokeWidth={1.8} />],
];

function pickIcon(icons: React.ReactNode[], id: unknown) {
  const n = typeof id === "number" ? id : 0;
  return icons[Math.abs(n) % icons.length];
}

const MISSION_TINT: Record<string, string> = {
  Main: "border-gold/60 bg-gold/10 text-gold",
  Daily: "border-aurora/60 bg-aurora/10 text-aurora",
  Repeatable: "border-crystal/60 bg-crystal/10 text-crystal",
  Adventure: "border-[#e8935a]/60 bg-[#e8935a]/10 text-[#e8935a]",
  Special: "border-ember/60 bg-ember/10 text-ember",
};

const TITLE_TINT: Record<string, string> = {
  "General Title": "border-raised bg-night text-mutedx",
  "Honor Title": "border-gold/60 bg-gold/10 text-gold",
  "Festival Title": "border-aurora/60 bg-aurora/10 text-aurora",
  "Book Title": "border-crystal/60 bg-crystal/10 text-crystal",
  "Prestige Title": "border-ember/60 bg-ember/10 text-ember",
};

function MapThumb({
  code, name, size, className,
}: {
  code: string; name?: string; size: number; className?: string;
}) {
  const [err, setErr] = useState(false);
  if (err) {
    return (
      <Glyph
        tint="border-aurora/50 bg-aurora/10 text-aurora"
        size={size} className={className} label={name}
      >
        <MapIcon size="55%" strokeWidth={1.8} />
      </Glyph>
    );
  }
  return (
    // eslint-disable-next-line @next/next/no-img-element
    <img
      src={`/art/maps/${code}.webp`}
      alt={name ?? code}
      width={size}
      height={size}
      loading="lazy"
      onError={() => setErr(true)}
      className={cn("game-icon", className)}
    />
  );
}

/** Grande carte monde (page détail) — masquée si le fichier
 *  manque (toutes les maps n'ont pas de visuel). */
export function MapArt({
  code, alt, className,
}: {
  code: string; alt: string; className?: string;
}) {
  const [err, setErr] = useState(false);
  if (err) return null;
  return (
    // eslint-disable-next-line @next/next/no-img-element
    <img
      src={`/art/maps/${code}.webp`}
      alt={alt}
      loading="lazy"
      onError={() => setErr(true)}
      className={cn(
        "w-full max-w-lg rounded-md border border-raised", className,
      )}
    />
  );
}

/**
 * Vignette d'une entité wiki. `kind` = dataset, `code` = icône
 * du jeu éventuelle, `rec` = la ligne (pour mapCode, type,
 * category, materials…).
 */
export function EntityIcon({
  kind, code, name, rec, size = 40, className,
}: {
  kind: string;
  code?: string | null;
  name?: string | null;
  rec?: Rec;
  size?: number;
  className?: string;
}) {
  // 1. icône du jeu quand le dataset la fournit
  if (code) {
    return (
      <GameIcon code={code} name={name} size={size} className={className} />
    );
  }
  const r = rec ?? {};
  switch (kind) {
    case "maps":
      if (typeof r.mapCode === "string" && r.mapCode) {
        return (
          <MapThumb
            code={r.mapCode} name={name ?? undefined}
            size={size} className={className}
          />
        );
      }
      return (
        <Glyph tint="border-aurora/50 bg-aurora/10 text-aurora"
          size={size} className={className} label={name ?? undefined}>
          <MapIcon size="55%" strokeWidth={1.8} />
        </Glyph>
      );
    case "monsters": {
      const g = MONSTER_GLYPH[r.type as number] ?? MONSTER_GLYPH[1];
      return (
        <Glyph tint={g.tint} size={size} className={className}
          label={name ?? undefined}>
          {pickIcon(g.icons, r.id)}
        </Glyph>
      );
    }
    case "missions": {
      const tint =
        MISSION_TINT[r.type as string] ??
        "border-raised bg-night text-mutedx";
      // Glyphe par type de quête (main=étendard, daily=spiral…).
      const ty = r.type as string;
      const ic =
        ty === "Main" ? <Flag size="55%" strokeWidth={1.8} />
        : ty === "Daily" ? <Sparkles size="55%" strokeWidth={1.8} />
        : ty === "Book" || ty === "Special"
          ? <BookOpen size="55%" strokeWidth={1.8} />
          : <ScrollText size="55%" strokeWidth={1.8} />;
      return (
        <Glyph tint={tint} size={size} className={className}
          label={name ?? undefined}>
          {ic}
        </Glyph>
      );
    }
    case "titles": {
      const tint =
        TITLE_TINT[r.category as string] ??
        "border-raised bg-night text-mutedx";
      const cat = r.category as string;
      const ic =
        cat === "Honor Title" ? <Medal size="55%" strokeWidth={1.8} />
        : cat === "Prestige Title"
          ? <Trophy size="55%" strokeWidth={1.8} />
        : cat === "Book Title"
          ? <BookOpen size="55%" strokeWidth={1.8} />
          : <Award size="55%" strokeWidth={1.8} />;
      return (
        <Glyph tint={tint} size={size} className={className}
          label={name ?? undefined}>
          {ic}
        </Glyph>
      );
    }
    case "npcs": {
      // Icône par rôle (Shop→pièces, Quest→parchemin…) — rec.role
      // vient de t_npc (« Shop », « Quest »…).
      const role = String(r.role ?? "");
      const ic = NPC_GLYPH.find(([re]) => re.test(role))?.[1]
        ?? <Users size="55%" strokeWidth={1.8} />;
      return (
        <Glyph tint="border-crystal/60 bg-crystal/10 text-crystal"
          size={size} className={className} label={name ?? undefined}>
          {ic}
        </Glyph>
      );
    }
    case "crafts": {
      // La miniature = l'icône du 1er matériau — une vraie image.
      const mats = r.materials as { icon?: string }[] | undefined;
      const matIcon = mats?.find((m) => m.icon)?.icon;
      if (matIcon) {
        return (
          <GameIcon
            code={matIcon} name={name} size={size} className={className}
          />
        );
      }
      return (
        <Glyph tint="border-[#e8935a]/60 bg-[#e8935a]/10 text-[#e8935a]"
          size={size} className={className} label={name ?? undefined}>
          <Hammer size="55%" strokeWidth={1.8} />
        </Glyph>
      );
    }
    default:
      // Dernier recours : gemme neutre, pas une initiale.
      return (
        <Glyph tint="border-raised bg-night text-mutedx"
          size={size} className={className} label={name ?? undefined}>
          <Gem size="55%" strokeWidth={1.8} />
        </Glyph>
      );
  }
}
