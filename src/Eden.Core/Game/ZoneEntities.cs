// Copyright (c) 2026 Noah Lhommedieu. MIT License.

using EdenClient.Combat;
using EdenClient.Net;
using EdenClient.Player;
using EdenClient.Protocol;

namespace EdenClient.Game;

/// <summary>
/// Kind natif d'une entité en vue (champ u8 du 298) — port de la
/// convention <c>RemoteEntity.Kind</c> de la référence Godot.
/// </summary>
public enum EntKind : byte
{
    /// <summary>Joueur distant (265, kind implicite 0) ou trésor.</summary>
    Player = 0,
    /// <summary>PNJ (298 kind 8).</summary>
    Npc = 8,
    /// <summary>Monstre (298 kind 12).</summary>
    Monster = 12,
}

/// <summary>Classification « danger » d'un mob pour la couleur du
/// nameplate — port de <c>MobDanger.Classify</c> (gris trop bas
/// niveau, rouge agressif/élite, jaune clair attaquable passif).</summary>
public enum EntDanger
{
    /// <summary>Niveau joueur inconnu ou template absent.</summary>
    Unknown,
    /// <summary>Mob ≥ 8 niveaux sous le joueur (CheckLevel natif).</summary>
    LowLevel,
    /// <summary>Mob qui peut aggro (élite/boss ou seek c_ai).</summary>
    Aggressive,
    /// <summary>Attaquable mais non agressif.</summary>
    Passive,
}

/// <summary>
/// Une entité réseau distante de la zone (mob, PNJ, joueur, trésor) —
/// modèle pur : positions en coords Eden natives (x, y sol, z haut),
/// interpolation via <see cref="InterpBuffer"/> (poussé en layout
/// Y-up x/−y/z pour que la correction de dérive utilise les axes
/// horizontaux, identique à la référence Godot).
/// </summary>
public sealed class ZoneEntity
{
    /// <summary>Sight-handle réseau (charId joueur, 0x88C8xxxx PNJ,
    /// 0xFFFFxxxx mob, id table trésor).</summary>
    public uint Handle;
    /// <summary>Template id (monsterId/npcId/itemId trésor, 0 joueur).</summary>
    public int EntityId;
    /// <summary>8 = PNJ, 12 = monstre, 0 = joueur/trésor.</summary>
    public EntKind Kind;
    /// <summary>Trésor au sol (329) — partage kind 0 avec les joueurs.</summary>
    public bool IsTreasure;
    /// <summary>Familier invoqué (entityId wire −2537, kind 12).</summary>
    public bool IsElfPet;
    /// <summary>charId du maître d'un elf-pet (wire Unk48/+0x48).</summary>
    public int OwnerCharId;
    /// <summary>Position Eden courante (x, y sol, z haut).</summary>
    public float X, Y, Z;
    /// <summary>Direction wire (radians, convention <see cref="EdenDir"/>).</summary>
    public float Dir;
    /// <summary>Vitesse annoncée au spawn (298 FieldSpeed / 265
    /// MoveSpeed) — horodate la destination prédite du 326.</summary>
    public float MoveSpeed;
    /// <summary>HP courant / max (280 attr 0 et 0x11).</summary>
    public long Hp, MaxHp;
    /// <summary>Nom résolu (tables FR, DisplayName serveur ou repli).</summary>
    public string DisplayName = "";
    /// <summary>Texte « say » en cours (268 — vide = rien).</summary>
    public string SayText = "";
    /// <summary>Stem modèle (M025/m014…) ou lettre corps perso (g/b…).</summary>
    public string ModelStem = "";
    /// <summary>Texture variante npclist col9 (« m00203.dds », vide = défaut).</summary>
    public string SkinTex = "";
    /// <summary>Échelle du modèle (npclist col2 / <c>MobScale</c>).</summary>
    public float ModelScale = 1f;
    /// <summary>Niveau (mob/NPC/joueur) — libellé « Lv n ».</summary>
    public int Level;
    /// <summary>Danger relatif au joueur (kind 12) — couleur du nom.</summary>
    public EntDanger Danger = EntDanger.Unknown;
    /// <summary>Buffer d'interpolation réseau.</summary>
    public readonly InterpBuffer Interp = new();
    /// <summary><see cref="Now"/> du dernier 326 — passé ~0,45 s sans
    /// repush l'input « moving » est périmé (pas de trame d'arrêt).</summary>
    public double LastMoveAt;
    /// <summary>Anim de déplacement courante (« move01 »…).</summary>
    public string MoveAnim = "move01";
    /// <summary>Anim de repos (« stand01 »).</summary>
    public string IdleAnim = "stand01";
    /// <summary>Vrai quand l'entité a reçu sa mort (272 fatal / 305 /
    /// HP 0) — le despawn 269 obtient une grâce pour l'anim death.</summary>
    public bool Dying;
    /// <summary>Apparence 32 o d'un perso (263) — lettre corps.</summary>
    public byte[] Appearance = [];
    /// <summary>loc → itemId de l'équipement porté (263 set0 + 276).</summary>
    public readonly Dictionary<int, int> WornItems = new();
    /// <summary>rideKey de monture (263 bit6 — visuel monture non porté).</summary>
    public int RideKey;

    /// <summary>Séquence de mort : fige l'état (l'anim est demandée
    /// via l'évènement <see cref="ZoneEntityRegistry.Died"/>).</summary>
    public void Die()
    {
        if (Dying) return;
        Dying = true;
        Hp = 0;
    }

    /// <summary>Respawn : défait l'état mourant (un 298 peut réutiliser
    /// le handle avant le 269 — filet de sécurité de la référence).</summary>
    public void Revive()
    {
        Dying = false;
    }
}

/// <summary>
/// Registre des entités réseau de la zone courante : sight-handle →
/// <see cref="ZoneEntity"/>. Port pur (hors moteur) de
/// <c>EntityRegistry.cs</c> Godot — même sémantique wire, évènements
/// consommés par la vue Flax (<c>EntitiesBridge</c>) ou les tests.
/// Toutes les méthodes <c>Handle*</c> s'appellent main-thread.
/// </summary>
public sealed class ZoneEntityRegistry
{
    private readonly Dictionary<uint, ZoneEntity> _entities = new();

    /// <summary>Horloge injectée (secondes) — <see cref="InterpBuffer"/>.</summary>
    public Func<double> Now = () => 0.0;

    /// <summary>Hauteur du sol Eden (x, y sol) → z — injectée depuis
    /// le terrain chargé (le z serveur est souvent absent/périmé :
    /// écart ≤ 4 m = recalé, au-delà = plateforme/volant conservé,
    /// règle M10 de la référence).</summary>
    public Func<float, float, float?>? GroundHeight;

    /// <summary>Gabarit c_monster par monsterId — injecté
    /// (<see cref="Data.Tables.MonsterTable"/> chargé par le bridge).</summary>
    public Func<int, Data.Tables.MonsterTable.MonsterDef?>? MonsterDef;

    /// <summary>Ligne scène d'un monsterId (repli modèle + nom) —
    /// injectée (<see cref="Data.Tables.SceneTable.Scene"/>).</summary>
    public Func<int, Data.Tables.SceneTable.MonsterSpawn?>? SceneMonster;

    /// <summary>Ligne scène d'un npcId (repli modèle + nom) —
    /// injectée.</summary>
    public Func<int, Data.Tables.SceneTable.NpcSpawn?>? SceneNpc;

    /// <summary>Vrai si le stem résout un modèle
    /// (« monster/model/&lt;stem&gt;.nif ») — le bridge teste le VFS.</summary>
    public Func<string, bool>? ModelExists;

    /// <summary>Résolution modèle+nom+niveau d'un elf-pet par
    /// ownerCharId (le wire ne porte pas le template).</summary>
    public Func<int, (string Model, string Name, int Level)?>?
        ResolveElfPet;

    /// <summary>Nom d'item pour le libellé des trésors (t_item).</summary>
    public Func<int, string?>? ItemName;

    /// <summary>Level du joueur local — danger des mobs.</summary>
    public int PlayerLevel;

    /// <summary>Chance de détection c_ai col2 par aiId — injectée
    /// (table chargée par le bridge ; 0 si absente).</summary>
    public Func<int, int>? AiSeekChance;
    /// <summary>charId du joueur local (0 = inconnu) — les trames
    /// visant le self sont ignorées (déjà spawné par le chargement).</summary>
    public uint SelfCharId;

    // --------------------------------------------------------------
    // Évènements consommés par la vue (thread principal).

    /// <summary>Spawn (ré)appliqué — après maj du modèle.</summary>
    public event Action<ZoneEntity>? Spawned;
    /// <summary>Despawn (269/294/330/migration) — avant suppression
    /// effective du modèle ; la vue reçoit le handle.</summary>
    public event Action<ZoneEntity>? Despawned;
    /// <summary>Stem/skin/échelle du modèle a changé — rebuild visuel.</summary>
    public event Action<ZoneEntity>? VisualChanged;
    /// <summary>Libellé/couleur à rafraîchir.</summary>
    public event Action<ZoneEntity>? NameChanged;
    /// <summary>HP/maxHp modifiés (280).</summary>
    public event Action<ZoneEntity>? VitalsChanged;
    /// <summary>Mort constatée (305, 272 fatal) — jouer l'anim death.</summary>
    public event Action<ZoneEntity>? Died;
    /// <summary>Respawn d'une entité mourante (298 re-vu).</summary>
    public event Action<ZoneEntity>? Revived;
    /// <summary>Anim one-shot demandée (272/495) — candidats dans
    /// l'ordre de préférence.</summary>
    public event Action<ZoneEntity, IReadOnlyList<string>>? AnimRequested;
    /// <summary>Bulle « say » (268).</summary>
    public event Action<ZoneEntity, string>? SayRequested;
    /// <summary>Position self reçue (264) — x, y Eden.</summary>
    public event Action<float, float>? SelfMoved;
    /// <summary>WhoIs à émettre (charId — apparence perso distant).</summary>
    public event Action<uint>? WhoisRequested;

    /// <summary>Toutes les entités connues.</summary>
    public IReadOnlyDictionary<uint, ZoneEntity> Entities => _entities;
    /// <summary>Nombre d'entités vivantes.</summary>
    public int Count => _entities.Count;

    /// <summary>Grâce accordée à un cadavre lors d'un despawn pendant
    /// que l'anim death tourne (le 269 natif arrive ~5 s après le 305 —
    /// cette marge ne couvre que la fin d'anim).</summary>
    public float CorpseGraceSec = 2.0f;

    private readonly Dictionary<uint, byte> _whoisSent = new();

    // ------------------------------------------------------------------
    // helpers internes

    /// <summary>Snap du sol comme la référence : si z serveur absent
    /// ou à moins de 4 m du terrain, le terrain fait foi.</summary>
    private float GroundZ(float ex, float ey, float ez)
    {
        if (GroundHeight?.Invoke(ex, ey) is { } h &&
            (ez == 0f || ez < h || ez - h <= 4f))
            return h;
        return ez;
    }

    /// <summary>Pousse un état Eden (x, y sol, z haut) dans le buffer —
    /// layout Y-up (x, z, −y) : axes horizontaux x/z pour la
    /// correction de dérive de <see cref="InterpBuffer.PushMovement"/>,
    /// la vue relit (x, −z, y) = (ex, ey, ez).</summary>
    private void PushState(ZoneEntity e, float ex, float ey, float ez,
        float dir)
    {
        ez = GroundZ(ex, ey, ez);
        e.X = ex; e.Y = ey; e.Z = ez; e.Dir = dir;
        e.Interp.Push(Now(), ex, ez, -ey, dir);
    }

    /// <summary>Prédiction cur→dst horodatée (326/267).</summary>
    private void PushMove(ZoneEntity e, float cx, float cy,
        float dir, double dstTime, float dx, float dy)
    {
        var cz = GroundZ(cx, cy, 0f);
        var dz = GroundZ(dx, dy, 0f);
        e.X = cx; e.Y = cy; e.Z = cz; e.Dir = dir;
        e.Interp.PushMovement(Now(), cx, cz, -cy, dir, dstTime,
            dx, dz, -dy);
    }

    private ZoneEntity GetOrCreate(uint handle, int entityId, EntKind kind)
    {
        if (_entities.TryGetValue(handle, out var e))
            return e;
        e = new ZoneEntity
        {
            Handle = handle,
            EntityId = entityId & 0xFFFF,
            Kind = kind,
        };
        _entities[handle] = e;
        return e;
    }

    /// <summary>Supprime le modèle et notifie le despawn. La grâce
    /// cadavre (anim death en cours) est gérée par la vue.</summary>
    private void Remove(uint handle)
    {
        if (!_entities.Remove(handle, out var e)) return;
        _whoisSent.Remove(handle);
        Despawned?.Invoke(e);
    }

    /// <summary>Clear total (fin de zone, 293 NodeExit migration).</summary>
    public void Clear()
    {
        foreach (var e in _entities.Values)
            Despawned?.Invoke(e);
        _entities.Clear();
        _whoisSent.Clear();
    }

    // ------------------------------------------------------------------
    // Handlers (appelés depuis le bridge — thread principal).

    /// <summary>298 SeeMob — spawn/mise à jour d'un mob ou PNJ.</summary>
    public void HandleSeeMob(ZoneSeeMob m)
    {
        var e = GetOrCreate(m.Handle, m.EntityId, (EntKind)m.Kind);
        var wasDying = e.Dying;
        e.Revive();
        if (wasDying) Revived?.Invoke(e);
        e.Hp = m.Hp;
        e.MaxHp = m.MaxHp;
        e.MoveSpeed = m.FieldSpeed;

        var modelStem = "";
        var skinTex = "";
        var label = m.DisplayName;
        var npcScale = 1f;
        // Familier invoqué : kind 12 + entityId fixe −2537, nom du pet
        // en DisplayName, charId du maître en Unk48.
        if (m.EntityId == -2537)
        {
            e.IsElfPet = true;
            e.OwnerCharId = m.Unk48;
            if (ResolveElfPet?.Invoke(m.Unk48) is { } ei)
            {
                modelStem = ei.Model;
                e.Level = ei.Level;
                if (label.Length == 0) label = ei.Name;
            }
            if (label.Length == 0) label = "Familiar";
        }
        else if (m.Kind == (byte)EntKind.Monster)
        {
            var id = m.EntityId & 0xFFFF;
            var spawn = SceneMonster?.Invoke(id);
            var def = MonsterDef?.Invoke(id);
            // c_monster d'abord (gabarit), stems candidats
            // (M363040 → M363/M36/M363040), scène en repli — ordre
            // prouvé de la référence (les ini contiennent des typos).
            if (def != null)
            {
                foreach (var stem in StemCandidates(def.ModelCode))
                    if (stem.Length > 0 &&
                        (ModelExists?.Invoke(stem) ?? true))
                    { modelStem = stem; break; }
                if (modelStem.Length == 0 &&
                    spawn != null && spawn.ModelCode.Length > 0 &&
                    (ModelExists?.Invoke(spawn.ModelCode) ?? false))
                    modelStem = spawn.ModelCode;
                if (modelStem.Length == 0)
                    modelStem = def.ModelCode.Length > 5
                        ? def.ModelCode[..^2] : def.ModelCode;
                e.Level = def.Level;
                e.Danger = EntDangerRules.Classify(def, PlayerLevel,
                    AiSeekChance?.Invoke(def.AiId) ?? 0);
                // Échelle visuelle des mobs (élite/boss plus gros).
                if (!def.IsStationary)
                    npcScale = Data.BreakPointUi.MobScale(
                        def.MonsterType, m.Handle);
            }
            else if (spawn != null && spawn.ModelCode.Length > 0 &&
                     (ModelExists?.Invoke(spawn.ModelCode) ?? false))
            {
                modelStem = spawn.ModelCode;
            }
            // t_monster.ini FR prioritaire — le DisplayName serveur
            // est Big5 et réécrirait le label à chaque re-SeeMob.
            var tn = MonsterName?.Invoke(id);
            if (tn != null) label = tn;
            else if (label.Length == 0)
                label = def?.Name ?? spawn?.Name ?? "";
            label = Data.BreakPointUi.FormatMobLabel(e.Level, label);
        }
        else if (m.Kind == (byte)EntKind.Npc)
        {
            var id = m.EntityId & 0xFFFF;
            // c_npc+npclist d'abord (stem + texture variante fidèles —
            // le « motion » de la scène n'est qu'une anim, symptôme
            // H4 de la référence) ; la scène sert de repli.
            if (NpcModelTable.Resolve(id) is { } nd)
            {
                modelStem = nd.Stem;
                skinTex = nd.Texture;
                if (nd.Scale > 0f) npcScale = nd.Scale;
            }
            var spawn = SceneNpc?.Invoke(id);
            if (modelStem.Length == 0 && spawn != null)
                modelStem = spawn.ModelCode;
            // t_npc.ini prioritaire sur le DisplayName serveur.
            var en = NpcName?.Invoke(id);
            if (en != null)
            {
                var title = NpcTitle?.Invoke(id);
                label = title != null ? $"<{title}>\n{en}" : en;
            }
            else if (label.Length == 0 && spawn != null)
                label = spawn.Name;
        }
        if (label.Length == 0) label = $"#{m.EntityId & 0xFFFF}";

        if (modelStem.Length > 0 &&
            (modelStem != e.ModelStem || skinTex != e.SkinTex))
        {
            e.ModelStem = modelStem;
            e.SkinTex = skinTex;
            e.ModelScale = npcScale;
            VisualChanged?.Invoke(e);
        }
        else if (npcScale != e.ModelScale && npcScale > 0f)
        {
            e.ModelScale = npcScale;
        }

        // FieldZ = DIRECTION (ent+0x58) — pas une hauteur.
        PushState(e, m.X, m.Y, 0f, m.FieldZ);
        if (e.DisplayName != label)
        {
            e.DisplayName = label;
            NameChanged?.Invoke(e);
        }
        VitalsChanged?.Invoke(e);
        Spawned?.Invoke(e);
    }

    /// <summary>265 SeeChar — spawn d'un joueur distant
    /// (handle = charId). Pas de blob d'apparence : WhoIs (NCID 2)
    /// une fois par révision modèle → la fiche 263 apporte le corps.</summary>
    public void HandleSeeChar(ZoneSeeChar c)
    {
        if (c.CharId == SelfCharId && SelfCharId != 0)
            return;   // joueur local — déjà spawné par le chargement map.
        var e = GetOrCreate(c.CharId, 0, EntKind.Player);
        e.Hp = c.Hp;
        e.MaxHp = c.MaxHp;
        e.MoveSpeed = c.MoveSpeed;
        e.Level = c.Level;
        PushState(e, c.X, c.Y, 0f, c.Facing);
        var label = $"Lv{c.Level} char#{c.CharId}";
        if (e.DisplayName != label)
        {
            e.DisplayName = label;
            NameChanged?.Invoke(e);
        }
        // Apparence : WhoIs une fois par modelRev (Revisions[0]).
        if (c.Revisions.Length > 0 &&
            (!_whoisSent.TryGetValue(c.CharId, out var rev) ||
             rev != c.Revisions[0]))
        {
            _whoisSent[c.CharId] = c.Revisions[0];
            WhoisRequested?.Invoke(c.CharId);
        }
        VitalsChanged?.Invoke(e);
        Spawned?.Invoke(e);
    }

    /// <summary>326 MovePrediction — déplacement cur→dst horodaté.
    /// Le client marche lui-même de cur vers dst à la vitesse de
    /// l'entité (une trame par trajet, MobWander).</summary>
    public void HandleMovePrediction(ZoneMovePrediction m)
    {
        if (!_entities.TryGetValue((uint)m.Handle, out var e) || e.Dying)
            return;
        var dir = m.FacingFloat;
        var dst = m.DstPos;
        var moving = dst.HasValue &&
            (dst.Value.X != m.CurPos.X || dst.Value.Y != m.CurPos.Y);
        if (moving && dst is { } d)
        {
            var ddx = d.X - m.CurPos.X;
            var ddy = d.Y - m.CurPos.Y;
            var dist = MathF.Sqrt(ddx * ddx + ddy * ddy);
            var speed = e.MoveSpeed > 0.5f ? e.MoveSpeed : 6f;
            PushMove(e, m.CurPos.X, m.CurPos.Y, dir,
                Now() + dist / speed, d.X, d.Y);
            // Record de direction natif du flag (3 gauche, 5 droite…).
            e.MoveAnim = DirectionalLocomotion.AnimForFlag(m.Flag);
        }
        else
        {
            PushState(e, m.CurPos.X, m.CurPos.Y, 0f, dir);
        }
        e.LastMoveAt = Now();
    }

    /// <summary>267 ClientGoto — « aller à » (aggro : position du
    /// joueur). Proche → marche depuis la position prédite ; loin
    /// (>20 u) ou jamais vu → téléport franc.</summary>
    public void HandleGoto(ZoneClientGoto m)
    {
        if (!_entities.TryGetValue((uint)m.Handle, out var e) || e.Dying)
            return;
        if (e.Interp.SampleAt(Now(), out var px, out var py,
                out var pz, out _))
        {
            var ddx = m.X - px;
            var ddy = m.Y - (-pz);
            var dist2 = ddx * ddx + ddy * ddy;
            if (dist2 <= e.Interp.MaxCurCorrectionSq)
            {
                var speed = e.MoveSpeed > 0.5f ? e.MoveSpeed : 6f;
                var dist = MathF.Sqrt(dist2);
                PushMove(e, px, -pz, e.Dir, Now() + dist / speed,
                    m.X, m.Y);
                return;
            }
        }
        PushState(e, m.X, m.Y, 0f, e.Dir);
    }

    /// <summary>264 ClientMoveChar [f32 x][f32 y] — écho self.</summary>
    public void HandleMoveChar(ZoneMoveChar m) =>
        SelfMoved?.Invoke(m.X, m.Y);

    /// <summary>269 LifeEradicate — despawn.</summary>
    public void HandleLifeEradicate(ZoneLifeEradicate m) =>
        Remove((uint)m.Handle);

    /// <summary>294 LeaveNode — despawn (NON PROUVE : même traitement
    /// que 269 si un handle est présent).</summary>
    public void HandleLeaveNode(ZoneLeaveNode m) =>
        Remove((uint)m.Value);

    /// <summary>293 NodeExit — pré-migration : vide le registre.</summary>
    public void HandleNodeExit(ZoneNodeExit m) => Clear();

    /// <summary>330 TreasureDisappear — retrait d'un trésor.</summary>
    public void HandleTreasureDisappear(ZoneTreasureDisappear m) =>
        Remove(m.Value);

    /// <summary>329 SeeTreasure — trésor au sol (handle = id table).</summary>
    public void HandleSeeTreasure(ZoneSeeTreasure t)
    {
        var e = GetOrCreate(t.Handle, t.ItemId, EntKind.Player);
        e.IsTreasure = true;
        PushState(e, t.X, t.Y, 0f, 0f);
        var iname = ItemName?.Invoke(t.ItemId);
        var label = iname != null
            ? Data.TreasureRules.Label(iname, t.Count)
            : $"treasure#{t.ItemId}×{t.Count}";
        if (e.DisplayName != label)
        {
            e.DisplayName = label;
            NameChanged?.Invoke(e);
        }
        Spawned?.Invoke(e);
    }

    /// <summary>280 UpdateAttr — attrId 0 = HP courant, 0x11 = HP max
    /// (sémantique H6 de la référence).</summary>
    public void HandleUpdateAttr(ZoneUpdateAttr m)
    {
        if (!_entities.TryGetValue((uint)m.Handle, out var e)) return;
        if (m.AttrId == 0)
        {
            e.Hp = m.Current;
            if (e.MaxHp < e.Hp) e.MaxHp = e.Hp;
            if (e.Hp <= 0 && !e.Dying)
            {
                e.Die();
                Died?.Invoke(e);
            }
            VitalsChanged?.Invoke(e);
        }
        else if (m.AttrId == 0x11)
        {
            e.MaxHp = m.Current;
            VitalsChanged?.Invoke(e);
        }
    }

    /// <summary>380 MobTransform — le mob change de template : on
    /// ré-évalue le modèle si possible.</summary>
    public void HandleMobTransform(ZoneMobTransform m)
    {
        if (!_entities.TryGetValue((uint)m.Handle, out var e)) return;
        e.EntityId = m.NewTemplateId & 0xFFFF;
        if (MonsterDef?.Invoke(e.EntityId) is { } def)
        {
            foreach (var stem in StemCandidates(def.ModelCode))
                if (stem.Length > 0 &&
                    (ModelExists?.Invoke(stem) ?? true))
                { e.ModelStem = stem; break; }
            e.Level = def.Level;
            e.Danger = EntDangerRules.Classify(def, PlayerLevel,
                AiSeekChance?.Invoke(def.AiId) ?? 0);
            VisualChanged?.Invoke(e);
            var label = Data.BreakPointUi.FormatMobLabel(
                def.Level,
                MonsterName?.Invoke(e.EntityId) ?? def.Name);
            if (e.DisplayName != label)
            {
                e.DisplayName = label;
                NameChanged?.Invoke(e);
            }
        }
    }

    /// <summary>Nom FR d'un monstre (t_monster) — injectée par le
    /// bridge (<see cref="Data.TextResolver.MonsterName"/>).</summary>
    public Func<int, string?>? MonsterName;

    /// <summary>Nom FR d'un PNJ (t_npc) — injectée.</summary>
    public Func<int, string?>? NpcName;

    /// <summary>Titre FR d'un PNJ (t_npc, « &lt;Titre&gt; ») — injectée.</summary>
    public Func<int, string?>? NpcTitle;

    /// <summary>Candidats de stem pour un code c_monster — ordre :
    /// ^2 (variante 2 chiffres), ^3 (convention npclist/partner),
    /// code brut.</summary>
    public static IEnumerable<string> StemCandidates(string code)
    {
        if (code.Length > 5) yield return code[..^2];
        if (code.Length > 4) yield return code[..^3];
        yield return code;
    }

    /// <summary>272 ClientMeleeAttack — anim d'attaque de l'attaquant
    /// (kfId→nom exact via motion.ini puis convention), hurt/death sur
    /// la cible — port de <see cref="CombatFx"/>.</summary>
    public void HandleMeleeAttack(ZoneClientMeleeAttack m)
    {
        if (_entities.TryGetValue(m.Attacker, out var atk) &&
            Combat.CombatFx.AttackAnim(
                Combat.MotionTable.Shared, atk.ModelStem, m.MotionKfId)
                is { } atkAnim)
            AnimRequested?.Invoke(atk, new[] { atkAnim });

        var hitAnim = Combat.CombatFx.HitAnim(m.HitType);
        if (Combat.CombatFx.IsFatal(m.HitType))
            MarkDying(m.Target);
        if (hitAnim != null &&
            _entities.TryGetValue(m.Target, out var tgt))
            AnimRequested?.Invoke(tgt, new[] { hitAnim });
    }

    /// <summary>495 ClientPlayMonsterAction — motion kfId sur l'entité
    /// (<see cref="Scene.MonsterActionAnim"/> : motion.ini exact puis
    /// convention).</summary>
    public void HandleMonsterAction(ZoneClientPlayMonsterAction m)
    {
        var handle = (uint)m.Field1;
        if (!_entities.TryGetValue(handle, out var e)) return;
        var names = Scene.MonsterActionAnim.Candidates(
            Combat.MotionTable.Shared, e.ModelStem, m.Field2);
        if (names.Count > 0)
            AnimRequested?.Invoke(e, names);
    }

    /// <summary>305 ClientDie — la victime joue son anim de mort.</summary>
    public void HandleClientDie(ZoneClientDie m) => MarkDying(m.Victim);

    /// <summary>272 fatal / 305 — marque mourante.</summary>
    private void MarkDying(uint handle)
    {
        if (_entities.TryGetValue(handle, out var e) && !e.Dying)
        {
            e.Die();
            Died?.Invoke(e);
        }
    }

    /// <summary>268 ClientSay — texte « say » d'une entité (bulle).</summary>
    public void HandleSay(ZoneClientSay m)
    {
        if (!_entities.TryGetValue(m.CharId, out var e) ||
            string.IsNullOrWhiteSpace(m.Message))
            return;
        e.SayText = m.Message;
        SayRequested?.Invoke(e, m.Message);
    }

    /// <summary>263 ClientDisplayChar — réponse WhoIs : apparence
    /// 32 o → lettre corps + nom réel + set0 d'équipement.</summary>
    public void HandleDisplayChar(ZoneClientDisplayChar m)
    {
        if (!_entities.TryGetValue(m.CharId, out var e) ||
            e.Kind != EntKind.Player)
            return;
        e.Appearance = m.Appearance;
        var stem = CharModelSpec.BodyStem(m.Appearance);
        if (stem != e.ModelStem)
        {
            e.ModelStem = stem;
            VisualChanged?.Invoke(e);
        }
        var label = $"Lv{m.Level} {m.CharName}";
        if (e.DisplayName != label)
        {
            e.DisplayName = label;
            NameChanged?.Invoke(e);
        }
        var (rideKey, worn) = ParseDisplayTail(m.Rest);
        e.RideKey = rideKey;
        e.WornItems.Clear();
        foreach (var kv in worn) e.WornItems[kv.Key] = kv.Value;
    }

    /// <summary>276 ClientChangeEquip — delta équipement d'un perso
    /// distant (stocké — le rendu des pièces n'est pas porté).</summary>
    public void HandleChangeEquip(ZoneChangeEquip m)
    {
        if (m.Container != 0 || (uint)m.CharId == SelfCharId ||
            !_entities.TryGetValue((uint)m.CharId, out var e) ||
            e.Kind != EntKind.Player)
            return;
        if (m.ItemId == 0) e.WornItems.Remove(m.Slot);
        else e.WornItems[m.Slot] = m.ItemId;
    }

    /// <summary>Queue variable du 263 — [u8 mask] + champs
    /// conditionnels (port verbatim de la référence) :
    /// <code>
    /// bit0 hp i64 | bit1 maxHp i64 | bit2 maxHp32 | bit3 maxMp
    /// bit4 +0x41 u8 | bit5 échelle u8 | bit6 rideKey i32
    /// bit7 transformTpl u32
    /// [u32 mask équipement set0] bit i = slot i (0..10) occupé, puis
    ///   par slot croissant : [u16 itemId] + corps EquipView
    /// </code></summary>
    public static (int RideKey, Dictionary<int, int> Worn)
        ParseDisplayTail(byte[] rest)
    {
        var worn = new Dictionary<int, int>();
        if (rest.Length < 1) return (0, worn);
        var r = new BinReader(rest);
        var mask = r.ReadByte();
        bool Skip(int bytes)
        {
            if (r.Remaining < bytes) return false;
            r.ReadBytes(bytes);
            return true;
        }
        if ((mask & 0x01) != 0 && !Skip(8)) return (0, worn);  // hp i64
        if ((mask & 0x02) != 0 && !Skip(8)) return (0, worn);  // maxHp i64
        if ((mask & 0x04) != 0 && !Skip(4)) return (0, worn);  // maxHp32
        if ((mask & 0x08) != 0 && !Skip(4)) return (0, worn);  // maxMp
        if ((mask & 0x10) != 0 && !Skip(1)) return (0, worn);  // +0x41 u8
        if ((mask & 0x20) != 0 && !Skip(1)) return (0, worn);  // échelle
        var rideKey = 0;
        if ((mask & 0x40) != 0)
        {
            if (r.Remaining < 4) return (0, worn);
            rideKey = r.ReadInt();                             // rideKey
        }
        if ((mask & 0x80) != 0 && !Skip(4)) return (rideKey, worn);
        if (r.Remaining < 4) return (rideKey, worn);
        var eqMask = r.ReadUInt();
        for (var loc = 0; loc < 11; loc++)
        {
            if ((eqMask & (1u << loc)) == 0) continue;
            if (r.Remaining < 2) return (rideKey, worn);
            var itemId = r.ReadUShort();
            if (!NativeItemRecord.TryReadEquipView(r, out _))
                return (rideKey, worn);         // corps tronqué
            worn[loc] = itemId;
        }
        return (rideKey, worn);
    }
}

/// <summary>Classification « danger » d'un mob — port pur de
/// <c>MobDanger.Classify</c> : élite/boss toujours rouge, ≥ 8 niveaux
/// sous le joueur gris, aggro possible rouge, sinon passif.</summary>
public static class EntDangerRules
{
    /// <summary>Seuil natif d'écart de niveau (mob « trop bas »).</summary>
    public const int LowLevelGap = 8;

    /// <summary>Classifie un mob — <paramref name="def"/> = gabarit
    /// c_monster (null = inconnu), <paramref name="seekChance"/> =
    /// profil c_ai col2 (0 si table absente).</summary>
    public static EntDanger Classify(
        Data.Tables.MonsterTable.MonsterDef? def, int playerLevel,
        int seekChance)
    {
        if (def == null) return EntDanger.Unknown;
        if (def.IsEliteOrBoss) return EntDanger.Aggressive;
        if (playerLevel <= 0) return EntDanger.Unknown;
        if (playerLevel - def.Level >= LowLevelGap) return EntDanger.LowLevel;
        var structure = def.AttackPower <= 1 || def.AttackRangeMax <= 0
            || def.AiId == 164;
        if (!structure && def.AggroCells > 0 && def.AiId > 0 &&
            seekChance > 0)
            return EntDanger.Aggressive;
        return EntDanger.Passive;
    }
}
