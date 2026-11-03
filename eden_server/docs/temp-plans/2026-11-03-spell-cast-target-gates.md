# Plan — gates natifs de ciblage des casts (NCID 9/16/17/59)

Date : 2026-11-03. Tâche : « un sort ne peut pas se lancer sans cible,
SAUF les formes natives qui n'en demandent pas ».

## Symptômes

- Client : `SpellZone.Plan` route `CastShape.Single` + sans cible →
  `CastToNowhere` (NCID 17) — message que le natif n'a PAS (shape 0
  n'accepte que `CastToLife`).
- Serveur : `HandleCastNowhereAsync` accepte toute forme → un 17 forgé
  avec un sort ciblé résout quand même (self-buff / résultat).
- Serveur : NCID 9 accepte `handle==0` (`ResolveLifeTarget` → soi) —
  le natif `GetLifeEntity(0)` → NULL → 0x5af.
- Serveur : aucun check de spec +0x1a ni de portée sur les sorts
  mono-cible vers un joueur (buff/soin distant forgé possible).

## Preuves natives (zone)

| Fonction | @ | Règle |
|---|---|---|
| `CSpellDBO::LoadAllSpellData` | 0x08380130 | shape +0x18 ← Param1(c21)+ShapeSub(c31) ; spec +0x1a ← Param2(c22)+Type(c19) |
| `CSpellFactory::CastToLife` | 0x081545a0 | formes {0,2,5,6} seulement → sinon 0x5aa (textindex 1450) |
| `CSpellFactory::CastToLocation` | 0x08154210 | formes {1,2,5,6} → sinon 0x5ab (1451) |
| `CSpellFactory::CastToNowhere` | 0x08153990 | forme 3 → sinon 0x5ac (1452) |
| `CSpellFactory::CastToNearby` | 0x081536d0 | forme 4 → sinon 0x5ac |
| `CheckCanCast` | 0x081526e0 | sort inconnu → 0x5a9 (1449, arg "%hu"=id) |
| `CSpellToLife::CastTest` | 0x084f04d0+ | cible NULL → 0x5af (1455) ; CanCastTarget(spec) param_5=0 → refus silencieux ; HowClose → 0x333 (819 trop loin) / 0x334 (820 trop près, +0x44=0 jamais) |
| outer range | — | `r_c + r_t + (+0x40 range) + attrMod(0) + 2.5` (DAT_08896c68=2.5) |

Refus = `TextOutputIndex` (316 `[s16 idx][str arg]`) PUIS 410
`ClientCommandFailReturn [i32 ncid][i32 spell]` — sauf CanCastTarget
(refus silencieux → 410 seul).

## Éditions

1. `src/Eden.Zone/Combat/SpellCastRouting.cs` (NEUF, pur/testable) :
   `ShapeOf(def)`, `Accepts(ncid,shape)`, `RefusalTextIndex`,
   `SpecAllowsPlayerTarget`, `SpecNeedsTeam`, `LifeCastOuterRange`.
2. `ZoneEntryFlow.Combat.Spells.cs` — HandleSpellHitAsync :
   def==null → 316[1449]+410 ; shape ∉{0,2,5,6} → 316[1450]+410 ;
   handle==0 → 316[1455]+410 ; cible joueur → spec (-99/-95/-94
   hostiles sur joueur → 410 silencieux), -96/-97 → équipe requise,
   portée `LifeCastOuterRange` → 316[819]+410.
3. `ZoneEntryFlow.Combat.Spells.Resolution.cs` — def==null → +316[1449] ;
   NCID 16 : shape ∉{1,2,5,6} → 316[1451]+410 ; NCID 17 : shape≠3 →
   316[1452]+410 ; NCID 59 : shape≠4 → 316[1452]+410 (ncid propre).
4. `ZoneEntryFlow.cs` — `case 59` séparé (410 refuse avec Field1=59).
5. Client `SpellZone.cs` — `CastRoute.Refused`, `Plan(Single,¬target)`
   → Refused (le natif n'a pas de message « vie dans le vide »).
6. Client `SkillCast.cs` — `SendCast` Refused → SysMsg textindex 2195
   « Veuillez sélectionner une cible pour utiliser cette compétence. »
   + aucun paquet émis. Repli combat-test : préfère un sort ≠ Single.
7. Tests : `NetTests/SpellZoneTests` (routes dont Refused),
   `Eden.Tests/SpellCastRoutingTests` (neuf — matrice NCID×shape,
   50015→shape3/17 OK, 50663→shape6/17 refus/16 OK, specs, portée).

## Hors scope

SpellFx, StatusWindow/buffs UI, montures, profil, archive.
`corpus/`, `re/`, `server_us/` en lecture seule.

## État final (2026-11-03 ~22h40)

- [x] SpellCastRouting.cs créé (routage NCID↔forme, refus, specs, portée)
- [x] NCID 9 : 1449/1450/1455 + cible joueur spec/team/range
- [x] NCID 16 : 1449/1451 ; NCID 17/59 : 1449/1452 (ncid propre)
- [x] Client : CastRoute.Refused + SysMsg 2195, aucun paquet
- [x] Tests : 62 SpellCastRoutingTests + SpellZoneTests maj — verts
- [x] Live : 50022 refusé local, 50015 (17) + 50663 (16) acceptés
- [x] Doc §8 + graphe régénéré
- [x] Fix collatéral : CastCircle.cs `Godot.Mesh.PrimitiveType`
      (fichier SpellFx d'un autre agent, cassait le build)
