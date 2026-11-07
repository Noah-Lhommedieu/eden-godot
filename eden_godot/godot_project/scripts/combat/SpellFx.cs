using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Game;
using EdenClient.Player;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;
using EdenClient.World;

namespace EdenClient.Combat;

/// <summary>
/// Effets visuels de sorts (lot « anims de skill »). Modèles
/// <c>effect/model/S&lt;code&gt;.glb</c> choisis par C_Spell
/// (<see cref="SpellFxTable"/>) :
///   - cast  (col 5, « E10121 » → S10121) : posé sous le lanceur à
///     278 (autre joueur) / 279 (self), suit le lanceur, durée du cast ;
///   - impact (col 10, « s10141 ») : posé sur la victime à 288/289
///     (blast) et à 281 (repli, dédoublonné), + hurt01 sur la cible.
/// Le glb porte une anim « embedded » jouée une fois puis le nœud est
/// libéré. Thread réseau → file → thread principal.
/// </summary>
public partial class SpellFx : Node
{
    public static SpellFx? Instance { get; private set; }

    /// <summary>Effets de sort actuellement à l'écran (plafond
    /// Settings « Max FX »).</summary>
    public static int ActiveFx { get; private set; }

    private readonly System.Collections.Concurrent.ConcurrentQueue<Action>
        _pending = new();
    private EdenSession? _session;
    private PlayerController? _player;
    private EntityRegistry? _registry;
    private bool _searched;
    // (spell,target) → date du dernier impact, pour fusionner 281/288/289.
    private readonly Dictionary<long, double> _lastImpact = new();

    private static readonly bool _fxHold = OS.GetCmdlineUserArgs()
        .Concat(OS.GetCmdlineArgs()).Contains("--fx-hold");

    public override void _Ready()
    {
        Instance = this;
        MiscPackets.Received += OnMisc;
        EntityBuffs.VisualAdded += OnBuffVisualAdded;
        EntityBuffs.VisualRemoved += OnBuffVisualRemoved;
        PlayerSkills.Defer = a => Callable.From(a).CallDeferred();
        PlayerSkills.Changed += WarmKnownSpellFx;
        if (OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs())
            .Contains("--fx-survey"))
            CallDeferred(nameof(Survey));
    }

    public override void _ExitTree()
    {
        MiscPackets.Received -= OnMisc;
        EntityBuffs.VisualAdded -= OnBuffVisualAdded;
        EntityBuffs.VisualRemoved -= OnBuffVisualRemoved;
        PlayerSkills.Changed -= WarmKnownSpellFx;
        _enchantFx.Clear();
        if (Instance == this) Instance = null;
    }

    private readonly HashSet<string> _warmedFx = new();

    /// <summary>Pré-parse en arrière-plan les .nif d'effets des sorts connus + level-up : le premier
    /// cast ne parse plus sur le thread principal (fx:s11711 = 32 ms mesurés). Chemins résolus ici
    /// (thread principal), parse sur le pool — GetNifCached est thread-safe.</summary>
    private void WarmKnownSpellFx()
    {
        var paths = new List<string>();
        void Add(SpellFxTable.Fx? f)
        {
            if (f is not { } d || !_warmedFx.Add(d.Model)) return;
            if (AssetCatalog.ResolveRawAsset($"effect/model/{d.Model}.nif") is { } p) paths.Add(p);
        }
        foreach (var k in PlayerSkills.Spells)
        {
            Add(SpellFxTable.CastFx(k.SpellMaster));
            Add(SpellFxTable.ReleaseFx(k.SpellMaster));
            Add(SpellFxTable.HitFx(k.SpellMaster));
        }
        Add(SpellFxTable.Resolve(LevelUpFx(false)));
        Add(SpellFxTable.Resolve(LevelUpFx(true)));
        if (paths.Count == 0) return;
        System.Threading.Tasks.Task.Run(() =>
        {
            foreach (var p in paths) AssetCatalog.GetNifCached(p);
        });
    }

    /// <summary>Visuels d'enchants actifs : (handle&lt;&lt;32 | eid) →
    /// nœud effet — l'effet col3 « BUFF特效 » tenu tant que le statut
    /// vit (flammes du DoT, étoiles du stun — 299 → pose, 300/purge →
    /// retrait). Le nœud est aussi libéré par sa propre durée quand le
    /// hold arrive à terme ; l'entrée ne sert qu'au retrait anticipé
    /// (dispel, mort, 300 serveur).</summary>
    private readonly Dictionary<long, Node3D> _enchantFx = new();

    /// <summary>299/301 : pose le visuel col3 de l'enchant sur
    /// l'entité (self → joueur, sinon entité du registry — le handle
    /// hors vue n'a simplement pas d'hôte).</summary>
    private void OnBuffVisualAdded(int handle, int eid, int restSec)
    {
        var key = EnchantTable.VisualKeyOf(eid);
        if (key == null) return;
        Node3D? host = handle == (int)(_registry?.SelfCharId ?? 0)
            ? _player
            : _registry != null &&
              _registry.Entities.TryGetValue((uint)handle, out var e)
                ? e : null;
        if (host == null) return;
        var k = ((long)handle << 32) | (uint)eid;
        if (_enchantFx.Remove(k, out var old) && IsInstanceValid(old))
            old.QueueFree();
        // Durée = reste du statut ; statut permanent → grand hold
        // (le retrait arrive par le 300 / la purge).
        var hold = restSec > 0 ? restSec : 86400f;
        if (Spawn(SpellFxTable.Resolve(key), host, hold) is { } n)
            _enchantFx[k] = n;
        GD.Print($"[spellfx] enchant {eid} «{key}» sur 0x{handle:x8} " +
                 $"({(restSec > 0 ? restSec + "s" : "perm.")})");
    }

    /// <summary>300/315/purge : retire le visuel de l'enchant.</summary>
    private void OnBuffVisualRemoved(int handle, int eid)
    {
        if (_enchantFx.Remove(((long)handle << 32) | (uint)eid,
                out var n) && IsInstanceValid(n))
            n.QueueFree();
    }

    /// <summary>Effet posé sur l'entité qui monte de niveau (284
    /// CNC_CZ_ClientLevelUp : flag u8 — 0 = niveau perso via
    /// GainExp @0x0841cb80, 1 = niveau de CLASSE via GainClassPoint
    /// @0x0841c140 — broadcast aux observateurs par le serveur, le
    /// client natif joue l'effet doré pour chacun). VERIFIED : le
    /// handler 284 natif (client_game FUN_00713430 @0x00713430)
    /// attache « s01651 » (niveau perso, + UI034.wav) et « s01751 »
    /// (niveau classe, + UI035.wav) sur l'entité, puis poste la
    /// notification NitificationTipWnd type 4/0xa pour le self.
    /// L'ancienne clé « s99211 » était un feu d'artifice nouvel-an
    /// (texte « 2011 ») — retirée.</summary>
    public static string LevelUpFx(bool classLevel) =>
        classLevel ? "s01751" : "s01651";

    private void OnMisc(NetMessage m)
    {
        if (m is not ZoneClientLevelUp u) return;
        // Field1 = charId de l'entité (handle joueur = charId).
        Node3D? host;
        if (u.Field1 == (_registry?.SelfCharId ?? 0)) host = _player;
        else if (_registry != null &&
                 _registry.Entities.TryGetValue(u.Field1, out var e))
            host = e;
        else return;   // hors vue : pas d'effet à poser
        if (host == null) return;
        var key = LevelUpFx(u.Field2 == 1);
        var fx = SpellFxTable.Resolve(key);
        GD.Print($"[spellfx] level-up {(u.Field2 == 1 ? "classe" : "perso")} " +
                 $"lv{u.Field3} char#{u.Field1} → {fx?.Model ?? key}");
        Spawn(fx, host, 0f);
    }

    /// <summary>--fx-survey : taille (m) max de chaque effet de sort,
    /// animation échantillonnée — détecte les effets démesurés.
    /// Sortie CSV « [fx-survey] code,scale,maxDim ».</summary>
    private void Survey()
    {
        var seen = new HashSet<string>();
        foreach (var (_, c, h) in SpellFxTable.All())
            foreach (var d in new[] { c, h })
            {
                if (d is not { } def || !seen.Add(def.Model)) continue;
                if (!AssetCatalog.TryGetGlb($"effect/model/{def.Model}",
                        out var abs)) continue;
                var fx = AssetCatalog.LoadGlbNode(abs);
                if (fx == null) continue;
                AddChild(fx);
                fx.Scale = Vector3.One * def.Scale;
                var ap = fx.FindChildren("*", "AnimationPlayer", true, false)
                    .OfType<AnimationPlayer>().FirstOrDefault();
                var name = ap?.GetAnimationList().FirstOrDefault();
                float len = name != null ? (float)ap!.GetAnimation(name).Length : 0f;
                float best = 0f;
                for (var k = 0; k < 6; k++)
                {
                    if (ap != null && name != null)
                    {
                        ap.Play(name);
                        ap.Seek(len * k / 5f, true);
                    }
                    var box = new Aabb();
                    var first = true;
                    foreach (var n in fx.FindChildren("*", "MeshInstance3D",
                                 true, false))
                    {
                        if (n is not MeshInstance3D mi || mi.Mesh == null)
                            continue;
                        var b = fx.GlobalTransform.AffineInverse() *
                                mi.GlobalTransform * mi.Mesh.GetAabb();
                        box = first ? b : box.Merge(b);
                        first = false;
                    }
                    if (!first)
                        best = Math.Max(best,
                            Math.Max(box.Size.X, Math.Max(box.Size.Y,
                                box.Size.Z)) * def.Scale);
                }
                GD.Print($"[fx-survey] {def.Model},{def.Scale:0.##}," +
                         $"{best:0.0}");
                fx.QueueFree();
            }
        GD.Print("[fx-survey] fin");
        GetTree().Quit();
    }

    // --fx-show=<spellId,...> : rejoue cast+impact des sorts sur le
    // joueur toutes les 4 s (preuve visuelle Flameshot sans combat).
    private readonly List<int> _showSpells = OS.GetCmdlineUserArgs()
        .Concat(OS.GetCmdlineArgs())
        .Where(a => a.StartsWith("--fx-show="))
        .SelectMany(a => a[10..].Split(','))
        .Select(x => int.TryParse(x, out var v) ? v : 0)
        .Where(v => v > 0).ToList();
    private double _showTimer = -8;
    private int _showIdx;
    // --fx-shot=<chemin.png> [--fx-shot-at=<s>] : capture du viewport
    // <s> (défaut 1,8) secondes après le PREMIER spawn du banc — la
    // fenêtre exacte où l'effet est affiché (le --shot de GameScene
    // part trop tôt, ~4 s, avant le 1er cycle à ~12 s). Le cercle
    // d'incantation vit 0→cast, release+impact après — choisir
    // l'instant selon la phase prouvée. --fx-quit : quitter ensuite.
    private static readonly string _fxShot = OS.GetCmdlineUserArgs()
        .Concat(OS.GetCmdlineArgs())
        .Where(a => a.StartsWith("--fx-shot="))
        .Select(a => a[10..]).FirstOrDefault() ?? "";
    private static readonly float _fxShotDelay = OS.GetCmdlineUserArgs()
        .Concat(OS.GetCmdlineArgs())
        .Where(a => a.StartsWith("--fx-shot-at="))
        .Select(a => float.TryParse(a[13..],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var v) ? v : 0f).FirstOrDefault() is { } d && d > 0
        ? d : 1.8f;
    private static readonly bool _fxQuit = OS.GetCmdlineUserArgs()
        .Concat(OS.GetCmdlineArgs()).Contains("--fx-quit");
    private ulong _fxShotAt;
    private bool _fxShotDone;
    private bool _townSent;
    private static readonly bool _fxTown = OS.GetCmdlineUserArgs()
        .Concat(OS.GetCmdlineArgs()).Contains("--fx-town");

    public override void _Process(double delta)
    {
        if (_fxTown && !_townSent && _session?.Zone?.IsConnected == true &&
            _player != null)
        {
            // --fx-town : preuve visuelle sans mobs/clones alentour.
            _townSent = true;
            _ = _session.SendZoneAsync(new ZoneTextCommand
            { Text = "//transport_node 1" });
            _showTimer = -25;
        }
        if (_showSpells.Count > 0 && _player != null)
        {
            _showTimer += delta;
            if (_showTimer >= 4)
            {
                _showTimer = 0;
                var id = _showSpells[_showIdx++ % _showSpells.Count];
                GD.Print($"[fx-show] spell #{id} " +
                         $"begin={SpellFxTable.BeginFx(id)} " +
                         $"cast={SpellFxTable.CastFx(id)} " +
                         $"release={SpellFxTable.ReleaseFx(id)} " +
                         $"hit={SpellFxTable.HitFx(id)} " +
                         $"chantKf={SpellCatalog.ChantKf(id)}");
                // Phases natives du sort : début (col4) → incantation
                // (col5) → zone/release (col6) → impact victime (col10)
                // — décalés pour être lus.
                var sid = id;
                var timed = SpellCatalog.CastDs(id) > 0 ? 1.6f : 1.2f;
                BeginCastFx(id, _player, timed);
                GetTree().CreateTimer(timed).Timeout += () =>
                {
                    EndCastFx(_player);
                    ReleaseAt(sid, _player, PlayerEden(), 0x7FFF_FFFF);
                    Spawn(SpellFxTable.HitFx(sid), _player, 0f);
                };
                if (_fxShot.Length > 0 && _fxShotAt == 0)
                    _fxShotAt = Time.GetTicksMsec() +
                        (ulong)(_fxShotDelay * 1000);
            }
        }
        if (_session == null && SessionCarry.Session is { } s) Bind(s);
        if (!_searched)
        {
            _searched = true;
            _player = GetTree().Root.FindChild("Player", true, false)
                as PlayerController;
            _registry = GetTree().Root.FindChild("NetEntities", true, false)
                as EntityRegistry;
        }
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex) { GD.PrintErr($"[spellfx] {ex.Message}"); }
        }
        if (!_fxShotDone && _fxShotAt != 0 &&
            Time.GetTicksMsec() >= _fxShotAt)
        {
            _fxShotDone = true;
            RenderingServer.ForceDraw();
            var img = GetViewport().GetTexture()?.GetImage();
            img?.SavePng(_fxShot);
            GD.Print($"[fx-shot] {_fxShot}");
            if (_fxQuit) GetTree().Quit();
        }
    }

    private void Bind(EdenSession s)
    {
        _session = s;
        s.OnZone(278, r =>
        {
            var m = new ZoneClientSpellCast();
            m.Deserialize(r);
            _pending.Enqueue(() => OnOtherCast(m));
        });
        s.OnZone(279, r =>
        {
            var m = new ZoneClientSpellCastOK();
            m.Deserialize(r);
            _pending.Enqueue(() => OnSelfCast(m));
        });
        s.OnZone(281, r =>
        {
            var m = new ZoneClientSpellHitToLife();
            m.Deserialize(r);
            _pending.Enqueue(() => Impact((ushort)m.SpellId,
                (uint)m.Target, true, (uint)m.Caster));
        });
        s.OnZone(288, r =>
        {
            var m = new ZoneClientSpellBlastOK();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                // Le 288 n'arrive qu'au lanceur. Formes directionnelles
                // (secteur/croix/direction) : le visuel de blast émane
                // DU LANCEUR vers la pointe (X,Y) — sommet du cône /
                // centre de la croix / origine de l'onde natifs — pas
                // posé sur la victime.
                var id = (ushort)m.SpellId;
                EndCastFx(_player);
                ReleaseForBlast(id, _player, PlayerEden(),
                    (uint)m.VictimHandle);
                if (Directional(id))
                    DirectionalBlast(id, PlayerEden(), (m.X, m.Y),
                        PlayerFacingEden());
                else
                    Impact(id, (uint)m.VictimHandle, false,
                        (uint)(_registry?.SelfCharId ?? 0));
            });
        });
        s.OnZone(289, r =>
        {
            var m = new ZoneClientSpellBlastToLife();
            m.Deserialize(r);
            // Layout réel : champ 3 = LANCEUR, champ 7 = CIBLE, champ 11 = kfId de blast résolu.
            _pending.Enqueue(() =>
            {
                RemoteBlastAnim((uint)m.Caster, m.BlastIdx, (ushort)m.SpellId);
                var id = (ushort)m.SpellId;
                RemoteEntity? caster = null;
                if (_registry != null)
                    _registry.Entities.TryGetValue((uint)m.Caster, out caster);
                EndCastFx(caster);
                ReleaseForBlast(id, caster, (m.X, m.Y),
                    (uint)m.TargetHandle);
                if (Directional(id))
                    DirectionalBlast(id, (m.X, m.Y),
                        (m.Field8, m.Field9), 0f);
                else
                    Impact(id, (uint)m.TargetHandle, false,
                        (uint)m.Caster);
            });
        });
        s.OnZone(297, r =>
        {
            var m = new ZoneClientSpellBlastToLocation();
            m.Deserialize(r);
            _pending.Enqueue(() => OnGroundBlast(m));
        });
        GD.Print("[spellfx] binds 278/279/281/288/289/297 posés");
    }

    /// <summary>297 SpellBlastToLocation : effet de blast posé au point
    /// d'ancrage, orienté sur l'axe lanceur → point.
    /// Cercle au sol : ancrage = point visé (centre). Formes
    /// directionnelles (secteur/croix/direction) : ancrage = POSITION DU
    /// LANCEUR — le sommet du cône, le centre de la croix et l'origine de
    /// l'onde sont au lanceur dans la géométrie native
    /// (HitTest @0x84fb410/@0x84f5020/@0x84f6ad0) — le point ne sert
    /// qu'à l'orientation.</summary>
    private void OnGroundBlast(ZoneClientSpellBlastToLocation m)
    {
        var id = (ushort)m.SpellId;
        var dir = Directional(id);
        // Ancrage : lanceur (src) pour les formes directionnelles, point
        // visé (dst) pour le cercle au sol.
        var px = dir ? m.SrcX : m.DstX;
        var py = dir ? m.SrcY : m.DstY;
        // Hauteur : sol « marchable » du joueur (même source que ses pas), repli registre puis Y joueur.
        var hp = _player?.GroundHeight?.Invoke(px, -py);
        var hr = _registry?.GroundHeight?.Invoke(px, -py);
        var h = hp ?? hr ?? _player?.GlobalPosition.Y ?? 0f;
        var (ax, ay) = SpellZone.AxisTo(m.SrcX, m.SrcY, m.DstX, m.DstY, 0f);
        var anchor = new Node3D { Name = $"GroundFx_{id}" };
        AddChild(anchor);
        anchor.GlobalPosition = new Vector3(px, h, -py);
        anchor.Rotation = new Vector3(0f, MathF.Atan2(-ax, ay), 0f);   // même formule qu'EdenDir.ToYaw
        GD.Print($"[spellfx] zone #{id} forme={(CastShape)SpellCatalog.Shape(id)} " +
                 $"src=({m.SrcX:0.#},{m.SrcY:0.#}) dst=({m.DstX:0.#},{m.DstY:0.#}) " +
                 $"ancre=({px:0.#},{py:0.#}) h={h:0.#} " +
                 $"(marchable={hp?.ToString("0.#") ?? "-"} registre={hr?.ToString("0.#") ?? "-"} joueurY={_player?.GlobalPosition.Y:0.#}) " +
                 $"axe=({ax:0.##},{ay:0.##}) rayon={SpellZone.AreaRadius(SpellCatalog.RadiusUnits(id)):0.#}");
        // Centre de zone (cercle au sol, cercle auto-centré) : l'effet
        // de ZONE natif (C_Spell col 6, « s21622 » — tous les sorts de
        // forme 1 en ont un) y est posé ; à défaut l'effet d'impact
        // (col 10). Formes directionnelles : impact orienté depuis le
        // lanceur. Les victimes gardent leur col 10 (281).
        Spawn(dir ? SpellFxTable.HitFx(id)
            : SpellFxTable.ReleaseFx(id) ?? SpellFxTable.HitFx(id),
            anchor, 0f);
        RemoteEntity? relCaster = null;
        var selfD = _player != null
            ? new Vector2(_player.GlobalPosition.X - m.SrcX,
                _player.GlobalPosition.Z + m.SrcY).LengthSquared() : 99f;
        if (_registry != null && selfD > 2.25f)
        {
            RemoteEntity? best = null; var bestD = 2.25f;
            foreach (var e in _registry.Entities.Values)
            {
                // Joueurs (kind 0, corps 1 lettre) ou mobs (kind 12) avec
                // un modèle — les trésors (kind 0, stem long) exclus.
                var ok = e.Kind == 0 && e.ModelStem.Length == 1 ||
                         e.Kind == 12 && e.ModelStem.Length > 0;
                if (!ok || !IsInstanceValid(e)) continue;
                var d = new Vector2(e.GlobalPosition.X - m.SrcX, e.GlobalPosition.Z + m.SrcY).LengthSquared();
                if (d < bestD) { bestD = d; best = e; }
            }
            if (best != null) RemoteBlastAnim(best.Handle, m.BlastId, id);
            relCaster = best;
        }
        // Le 297 ne porte pas le handle du lanceur : entité la plus
        // proche trouvée ci-dessus. L'incantation (cercle/chant) s'y
        // termine au blast.
        EndCastFx(selfD <= 2.25f ? _player : relCaster);
        GetTree().CreateTimer(6f).Timeout += () =>
        { if (IsInstanceValid(anchor)) anchor.QueueFree(); };
    }

    /// <summary>Vrai pour les formes ancrées au lanceur (sommet du
    /// secteur, centre de la croix, origine de l'onde) — le point ne
    /// sert qu'à l'orientation.</summary>
    private static bool Directional(int spellId) =>
        (CastShape)SpellCatalog.Shape(spellId) is CastShape.Direction
            or CastShape.Cross or CastShape.Sector;

    // Effets d'incantation en cours par lanceur (cercle magique col 4 /
    // col 5) — retirés au blast, à l'interruption ou à la fin du cast.
    private readonly Dictionary<Node3D, List<Node3D>> _castFx = new();

    /// <summary>Incantation native : l'effet de DÉBUT (C_Spell col 4,
    /// « E11611 » = le cercle magique doré/vert tournant au pied du
    /// lanceur — ref-shot TimedCastSkill) puis l'effet d'incantation
    /// (col 5). Sort temporisé : tenus pendant
    /// <paramref name="castSeconds"/>, retirés par
    /// <see cref="EndCastFx"/> ; instantané : flash ~1,2 s. Un sort
    /// temporisé sans col 4/5 reçoit le cercle générique natif
    /// (<see cref="FallbackCastCircle"/>), jamais un rendu procédural.</summary>
    public void BeginCastFx(int spellId, Node3D? host, float castSeconds)
    {
        if (host == null || !IsInstanceValid(host)) return;
        var timed = castSeconds > 0.05f;
        var begin = SpellFxTable.BeginFx(spellId);
        var cast = SpellFxTable.CastFx(spellId);
        if (timed && begin == null && cast == null)
            begin = SpellFxTable.Resolve(FallbackCastCircle);
        var hold = timed ? castSeconds + 0.3f : 1.2f;
        EndCastFx(host);
        var list = new List<Node3D>();
        if (Spawn(begin, host, hold) is { } b) list.Add(b);
        if (Spawn(cast, host, timed ? hold : 0f) is { } c) list.Add(c);
        if (timed && list.Count > 0) _castFx[host] = list;
    }

    /// <summary>Cercle d'incantation générique natif : s11611 (Earth Shock,
    /// ref-shot TimedCastSkill) — pour un sort temporisé sans effet col 4/5.</summary>
    public const string FallbackCastCircle = "s11611";

    /// <summary>Fin de l'incantation de <paramref name="host"/> : le cercle
    /// d'incantation disparaît (blast, interruption, fin du cast).</summary>
    public void EndCastFx(Node3D? host)
    {
        if (host == null || !_castFx.Remove(host, out var list)) return;
        foreach (var n in list)
            if (IsInstanceValid(n)) World.SlowFree.Free(n);
    }

    /// <summary>Effet de ZONE/release (C_Spell col 6 — « s11722 ») au
    /// blast, posé au CENTRE de l'AoE natif, une seule instance par blast :
    /// <list type="bullet">
    /// <item>forme 0 (cible, ex. Tornade 50027 « rayon de 5 mètres ») :
    /// sur la cible principale — UNE tornade, pas une par victime ;</item>
    /// <item>forme 3 (centrée lanceur) et directionnelles : au lanceur ;</item>
    /// <item>forme 1 (cercle au sol) : au point visé, voir
    /// <see cref="OnGroundBlast"/>.</item></list></summary>
    private void ReleaseForBlast(int spellId, Node3D? caster,
        (float X, float Y) casterPos, uint victim)
    {
        var shape = (CastShape)SpellCatalog.Shape(spellId);
        if (shape == CastShape.Ground) return;
        if (shape == CastShape.Single && victim != 0)
        {
            Node3D? host = null;
            if (_registry != null &&
                _registry.Entities.TryGetValue(victim, out var te))
                host = te;
            else if (victim == (_registry?.SelfCharId ?? 0u))
                host = _player;
            if (host != null) { ReleaseAt(spellId, host, casterPos, victim); return; }
        }
        ReleaseAt(spellId, caster, casterPos, 0x7FFF_FFFF);
    }

    /// <summary>Pose l'effet de release (col 6) sur <paramref name="entity"/>
    /// (sans entité : ancre au sol à <paramref name="edenPos"/>).
    /// Dédoublonné par sort+hôte (le 288 et le 297 d'un même blast
    /// peuvent arriver tous les deux).</summary>
    private void ReleaseAt(int spellId, Node3D? entity,
        (float X, float Y) edenPos, uint keyId)
    {
        var fx = SpellFxTable.ReleaseFx(spellId);
        if (fx == null) return;
        var now = Time.GetTicksMsec() / 1000.0;
        var key = ((long)spellId << 32) | keyId;
        if (_lastImpact.TryGetValue(key, out var t) && now - t < 0.4)
            return;
        _lastImpact[key] = now;
        if (entity != null && IsInstanceValid(entity))
        {
            Spawn(fx, entity, 0f);
            GD.Print($"[spellfx] release {fx.Value.Model} " +
                $"#{spellId} sur {entity.Name}");
            return;
        }
        var h = _player?.GroundHeight?.Invoke(edenPos.X, -edenPos.Y)
            ?? _registry?.GroundHeight?.Invoke(edenPos.X, -edenPos.Y)
            ?? _player?.GlobalPosition.Y ?? 0f;
        var anchor = new Node3D { Name = $"ReleaseFx_{spellId}" };
        AddChild(anchor);
        anchor.GlobalPosition = new Vector3(edenPos.X, h, -edenPos.Y);
        Spawn(fx, anchor, 0f);
        GD.Print($"[spellfx] release {fx.Value.Model} #{spellId} " +
            $"@({edenPos.X:0.#},{edenPos.Y:0.#})");
        GetTree().CreateTimer(6f).Timeout += () =>
        { if (IsInstanceValid(anchor)) anchor.QueueFree(); };
    }

    private void ReleaseAtCaster(int spellId, Node3D? entity,
        (float X, float Y) edenPos) =>
        ReleaseAt(spellId, entity, edenPos, 0x7FFF_FFFF);

    /// <summary>Position Eden (x,y sol) du lanceur local.</summary>
    private (float X, float Y) PlayerEden() => _player != null
        ? WorldConv.ToEdenGround(_player.GlobalPosition) : (0f, 0f);

    /// <summary>Cap Eden du perso (même conversion que
    /// <c>SkillCast.PlayerFacingEden</c>).</summary>
    private float PlayerFacingEden() =>
        EdenClient.Game.EdenDir.FromYaw(_player?.ModelYaw ?? 0f);

    /// <summary>Effet de blast directionnel ancré à la position du
    /// lanceur (srcX,srcY Eden), orienté vers la pointe (dstX,dstY) —
    /// formes 2/5/6 (secteur/croix/direction) : cône/croix/onde
    /// émanent du lanceur. Utilisé par le 288 (lanceur self — src =
    /// position du joueur, dst = champ position du paquet) et le 289
    /// (témoin — src/dst du paquet). <paramref name="fallbackDir"/> =
    /// cap Eden de repli quand src ≈ dst (point confondu).</summary>
    private void DirectionalBlast(int spellId, (float X, float Y) src,
        (float X, float Y) dst, float fallbackDir)
    {
        var hp = _player?.GroundHeight?.Invoke(src.X, -src.Y);
        var hr = _registry?.GroundHeight?.Invoke(src.X, -src.Y);
        var h = hp ?? hr ?? _player?.GlobalPosition.Y ?? 0f;
        var (ax, ay) = SpellZone.AxisTo(src.X, src.Y, dst.X, dst.Y, fallbackDir);
        var anchor = new Node3D { Name = $"DirFx_{spellId}" };
        AddChild(anchor);
        anchor.GlobalPosition = new Vector3(src.X, h, -src.Y);
        anchor.Rotation = new Vector3(0f, MathF.Atan2(-ax, ay), 0f);
        GD.Print($"[verify] blast dir #{spellId} " +
                 $"forme={(CastShape)SpellCatalog.Shape(spellId)} " +
                 $"src=({src.X:0.#},{src.Y:0.#}) dst=({dst.X:0.#},{dst.Y:0.#}) " +
                 $"axe=({ax:0.##},{ay:0.##}) h={h:0.#}");
        Spawn(SpellFxTable.HitFx(spellId), anchor, 0f);
        GetTree().CreateTimer(6f).Timeout += () =>
        { if (IsInstanceValid(anchor)) anchor.QueueFree(); };
    }

    /// <summary>Gestuelle de blast d'un lanceur distant : kfId résolu du paquet (repli : col3 du sort).</summary>
    private void RemoteBlastAnim(uint caster, int blastIdx, int spellId)
    {
        if (_registry == null || !_registry.Entities.TryGetValue(caster, out var e)) return;
        // Fin de l'incantation : le chant tenu (HoldAnim) est relâché
        // avant le clip de blast — sinon la boucle chant reprendrait
        // après le one-shot.
        if (e.Animator != null) e.Animator.HoldAnim = "";
        var kf = blastIdx > 0 ? blastIdx : SpellCatalog.ActionKf(spellId);
        var anim = MotionTable.Shared.Lookup(e.ModelStem, kf)
            ?? MotionTable.Shared.Lookup(e.ModelStem, SpellCatalog.ActionKf(spellId));
        if (anim != null) e.PlayOnce(anim);
    }

    private void OnSelfCast(ZoneClientSpellCastOK m)
    {
        if (m.Ok == 0 || _player == null) return;
        var id = (ushort)m.SpellId;
        // Effet de début de cast (col 4, famille s…11 — le cercle
        // magique d'incantation du ref-shot TimedCastSkill) + effet
        // d'incantation (col 5), tenus pendant tout le cast.
        BeginCastFx(id, _player, m.CastTimeDs / 10f);
    }

    private void OnOtherCast(ZoneClientSpellCast m)
    {
        var id = (ushort)m.SpellId;
        var caster = (uint)m.Caster;
        if (_registry == null ||
            !_registry.Entities.TryGetValue(caster, out var e)) return;
        // Anim d'action du lanceur distant : sort instantané → tout de suite ; sort TEMPORISÉ → la
        // gestuelle part au blast (289/297, kfId résolu), pas au début de l'incantation (le serveur
        // décale la résolution de swing = temps 'hit' à partir du blast).
        var castDs = SpellCatalog.CastDs(id);
        if (castDs == 0)
        {
            var anim = MotionTable.Shared.Lookup(e.ModelStem,
                SpellCatalog.ActionKf(id));
            if (anim != null) e.PlayOnce(anim);
        }
        else if (e.Animator != null)
        {
            // Incantation temporisée d'un lanceur distant (natif) :
            // gestuelle « chant » C_Spell col 2 (magicNN) tenue en
            // boucle jusqu'au blast + cercle magique doré sous lui.
            var chant = SpellCatalog.ChantKf(id) is { } ck && ck > 0
                ? MotionTable.Shared.Lookup(e.ModelStem, ck) : null;
            if (chant != null) e.Animator.HoldAnim = chant;
            var held = e;
            GetTree().CreateTimer(castDs / 10f + 1.5f).Timeout += () =>
            {
                // Filet : si le 289/297 (qui relâche HoldAnim via
                // RemoteBlastAnim) est perdu, on rend la locomotion.
                if (IsInstanceValid(held) && held.Animator != null)
                    held.Animator.HoldAnim = "";
            };
        }
        BeginCastFx(id, e, castDs / 10f);
    }

    /// <summary>Effet d'impact sur la victime + réaction hurt01.
    /// Formes auto-centrées (Self/Nearby — Cyclone 50015 PBAoE) : le
    /// visuel de hit entoure LE LANCEUR une fois (le cyclone tourne
    /// autour de lui), pas une copie par victime — l'ancre par victime
    /// affichait le sort « à l'envers » sur les cibles.</summary>
    private void Impact(int spellId, uint target, bool hitMsg,
        uint caster = 0)
    {
        var key = ((long)spellId << 32) | target;
        var now = Time.GetTicksMsec() / 1000.0;
        if (_lastImpact.TryGetValue(key, out var t) && now - t < 0.4)
            return;
        _lastImpact[key] = now;
        if (_lastImpact.Count > 256) _lastImpact.Clear();

        Node3D? host = null;
        if (_registry != null &&
            _registry.Entities.TryGetValue(target, out var e))
        {
            host = e;
            if (hitMsg && !e.Dying && SpellCatalog.TargetType(spellId) == 1)
                e.PlayOnce("hurt01");
        }
        else if (target == (_registry?.SelfCharId ?? 0u)) host = _player;
        if (SpellZone.SelfAnchored(
                (CastShape)SpellCatalog.Shape(spellId)))
        {
            // Une seule instance de l'effet AoE au lanceur pour tout
            // le blast (dédoublonné par sort — la rafale de 281 des
            // N victimes ne l'empile pas N fois). L'ancre est le
            // LANCEUR de ce blast (self → _player, distant → son
            // entité — jamais le joueur pour un AoE de mob).
            var wkey = ((long)spellId << 32) | caster | 0x7F00_0000L;
            if (_lastImpact.TryGetValue(wkey, out var t2) &&
                now - t2 < 0.4)
                return;
            _lastImpact[wkey] = now;
            if (caster == (_registry?.SelfCharId ?? 0u))
                host = _player ?? host;
            else if (_registry != null &&
                     _registry.Entities.TryGetValue(caster, out var c))
                host = c;
        }
        if (host == null) return;
        Spawn(SpellFxTable.HitFx(spellId), host, 0f);
    }

    /// <summary>Animation de matériau d'une couche d'effet (extras
    /// <c>fx</c> du glb : NiAlphaController / NiMaterialColorController /
    /// NiTextureTransformController natifs, perdus par glTF). Sans elle
    /// toutes les couches restaient visibles à alpha plein en même temps
    /// (amas de grands sprites).</summary>
    private sealed class Layer
    {
        public BaseMaterial3D Mat = null!;
        public float[][]? Alpha;      // [t, v]
        public float AlphaStop, AlphaFreq = 1f; public int AlphaCyc = 2;
        public float[][]? Color;      // [t, r, g, b]
        public float[][]? UvU, UvV, UvSU, UvSV;
        public float UvStop, UvFreq = 1f; public int UvCyc = 2;
        public Color BaseColor = Colors.White;
    }

    private static float[][]? Keys(Godot.Collections.Dictionary d, string key)
    {
        if (!d.ContainsKey(key)) return null;
        var arr = d[key].AsGodotArray();
        var res = new float[arr.Count][];
        for (var i = 0; i < arr.Count; i++)
        {
            var row = arr[i].AsGodotArray();
            res[i] = new float[row.Count];
            for (var j = 0; j < row.Count; j++)
                res[i][j] = (float)row[j].AsDouble();
        }
        return res;
    }

    /// <summary>Échantillonne des clés [t, v...] au temps t (borné
    /// ou bouclé selon le cycle natif : 0 boucle, 2 clamp).</summary>
    private static float Sample(float[][] k, int comp, float t, float stop,
        float freq, int cyc)
    {
        t *= freq;
        if (cyc == 0 && stop > 0) t %= stop;
        if (t <= k[0][0]) return k[0][comp];
        for (var i = 1; i < k.Length; i++)
            if (t <= k[i][0])
            {
                var u = (t - k[i - 1][0]) /
                        Math.Max(1e-5f, k[i][0] - k[i - 1][0]);
                return Mathf.Lerp(k[i - 1][comp], k[i][comp], u);
            }
        return k[^1][comp];
    }

    private static bool UnderBillboard(Node n, Node root)
    {
        for (var p = n.GetParent(); p != null && p != root; p = p.GetParent())
            if (p.HasMeta("extras") &&
                p.GetMeta("extras").AsGodotDictionary() is { } d &&
                d.ContainsKey("billboard")) return true;
        return false;
    }

    /// <summary>Look natif des effets : additif, sans éclairage ni
    /// écriture de profondeur (le toon-pass les rendait ternes) ;
    /// une <see cref="Layer"/> par couche animée.</summary>
    private static List<Layer> MakeAdditive(Node3D root)
    {
        var layers = new List<Layer>();
        foreach (var n in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (n is not MeshInstance3D mi || mi.Mesh == null) continue;
            Godot.Collections.Dictionary? fx = null;
            if (mi.HasMeta("extras") &&
                mi.GetMeta("extras").AsGodotDictionary() is { } ex &&
                ex.ContainsKey("fx"))
                fx = ex["fx"].AsGodotDictionary();
            var bb = UnderBillboard(mi, root);
            for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                var src = mi.GetActiveMaterial(i) as BaseMaterial3D;
                var m = src != null
                    ? (BaseMaterial3D)src.Duplicate()
                    : new StandardMaterial3D();
                m.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                m.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
                m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                m.NoDepthTest = false;
                m.DisableReceiveShadows = true;
                if (bb && !_noBillboard)
                {
                    m.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
                    m.BillboardKeepScale = true;
                }
                // L'alpha du glb vaut 0 (animé par le KF natif) : forcé
                // à 1 ; les couches à contrôleur d'alpha le recalculent
                // à chaque image (Layer.Alpha), le fondu global vient
                // de Spawn.
                m.AlbedoColor = new Color(m.AlbedoColor, 1f);
                var l = new Layer { Mat = m, BaseColor = m.AlbedoColor };
                if (fx != null && !_noLayers)
                {
                    l.Alpha = Keys(fx, "a");
                    if (fx.ContainsKey("am"))
                    {
                        var am = fx["am"].AsGodotDictionary();
                        l.AlphaStop = (float)am["stop"].AsDouble();
                        l.AlphaFreq = (float)am["freq"].AsDouble();
                        l.AlphaCyc = am["cyc"].AsInt32();
                    }
                    l.Color = Keys(fx, "c3") ?? Keys(fx, "c1");
                    if (fx.ContainsKey("uv"))
                        foreach (var u in fx["uv"].AsGodotArray())
                        {
                            var ud = u.AsGodotDictionary();
                            var k = Keys(ud, "k");
                            switch (ud["op"].AsInt32())
                            {
                                case 0: l.UvU = k; break;
                                case 1: l.UvV = k; break;
                                case 3: l.UvSU = k; break;
                                case 4: l.UvSV = k; break;
                            }
                            l.UvStop = (float)ud["stop"].AsDouble();
                            l.UvFreq = (float)ud["freq"].AsDouble();
                            l.UvCyc = ud["cyc"].AsInt32();
                        }
                }
                layers.Add(l);
                mi.SetSurfaceOverrideMaterial(i, m);
            }
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        return layers;
    }

    // Diagnostics : --fx-nolayers (sans animation de matériau),
    // --fx-nobillboard.
    private static readonly bool _noLayers = OS.GetCmdlineUserArgs()
        .Concat(OS.GetCmdlineArgs()).Contains("--fx-nolayers");
    private static readonly bool _noBillboard = OS.GetCmdlineUserArgs()
        .Concat(OS.GetCmdlineArgs()).Contains("--fx-nobillboard");

    /// <summary>Met à jour alpha/couleur/UV d'une couche au temps t.</summary>
    private static void Apply(Layer l, float t, float fade)
    {
        var a = 1f;
        if (l.Alpha != null)
            a = Mathf.Clamp(Sample(l.Alpha, 1, t, l.AlphaStop, l.AlphaFreq,
                l.AlphaCyc), 0f, 1f);
        var c = l.BaseColor;
        if (l.Color != null)
            c = new Color(
                Mathf.Clamp(Sample(l.Color, 1, t, l.AlphaStop, l.AlphaFreq,
                    l.AlphaCyc), 0f, 1f),
                Mathf.Clamp(Sample(l.Color, 2, t, l.AlphaStop, l.AlphaFreq,
                    l.AlphaCyc), 0f, 1f),
                Mathf.Clamp(Sample(l.Color, 3, t, l.AlphaStop, l.AlphaFreq,
                    l.AlphaCyc), 0f, 1f));
        l.Mat.AlbedoColor = new Color(c, a * fade);
        if (l.UvU != null || l.UvV != null || l.UvSU != null ||
            l.UvSV != null)
        {
            var ox = l.UvU != null
                ? Sample(l.UvU, 1, t, l.UvStop, l.UvFreq, l.UvCyc) : 0f;
            var oy = l.UvV != null
                ? Sample(l.UvV, 1, t, l.UvStop, l.UvFreq, l.UvCyc) : 0f;
            l.Mat.Uv1Offset = new Vector3(ox, oy, 0f);
            if (l.UvSU != null || l.UvSV != null)
                l.Mat.Uv1Scale = new Vector3(
                    l.UvSU != null ? Sample(l.UvSU, 1, t, l.UvStop,
                        l.UvFreq, l.UvCyc) : 1f,
                    l.UvSV != null ? Sample(l.UvSV, 1, t, l.UvStop,
                        l.UvFreq, l.UvCyc) : 1f, 1f);
        }
    }

    /// <summary>EDEN_NATIVE=fx : .nif d'origine + NativeFx (particules, contrôleurs) au lieu du GLB.
    /// Durée : <paramref name="hold"/> ou 3 s ; faux = repli GLB.</summary>
    private bool SpawnNative(SpellFxTable.Fx def, Node3D host, float hold,
        out Node3D? node)
    {
        var swLoad = System.Diagnostics.Stopwatch.StartNew();
        var fx = AssetCatalog.LoadNativeScene($"effect/model/{def.Model}", out var nif);
        if (swLoad.Elapsed.TotalMilliseconds > 15)
            GD.Print($"[fx-load] {def.Model} {swLoad.Elapsed.TotalMilliseconds:F0} ms (thread principal)");
        node = null;
        if (fx == null || nif == null) return false;
        node = fx;
        host.AddChild(fx);
        fx.Position = Vector3.Zero;
        // NifScene reste en repère NIF Z-up : sans la rotation −90°/X
        // (WorldConv.EdenRootEulerDeg — déjà appliquée par le banc
        // --fx-native et par les modèles/map), les effets plats
        // (cyclone 50015, cercle d'incantation) se dressaient à la
        // verticale et les entonnoirs (tornade 50027) couchaient sur
        // le côté — « cyclone totalement inversé ».
        fx.RotationDegrees = WorldConv.EdenRootEulerDeg;
        fx.Scale = Vector3.One * def.Scale;
        var swAtt = System.Diagnostics.Stopwatch.StartNew();
        EdenClient.Native.NativeFx.Attach(fx, nif, new EdenClient.Native.NativeFxOptions
        {
            TexPrefix = "effect/texture/",
            HandleBillboards = System.Environment.GetEnvironmentVariable("EDEN_NATIVE_BB") == "0",
        });
        if (System.Environment.GetEnvironmentVariable("EDEN_LAT_TRACE") == "1")
            GD.Print($"[lat] fx attach {def.Model} {swAtt.Elapsed.TotalMilliseconds:F1} ms");
        // Durée de vie : hold > 0 = tenu par l'appelant (flash 1,2 s
        // d'un sort instantané, durée de cast + 0,3) — PAS de plancher
        // à 3 s : un effet d'incantation bouclé (s11811, cercle vert,
        // émetteur cyc=Loop) restait visible ~1,8 s de trop après un
        // soin instantané. hold = 0 : fin native du dernier contrôleur
        // clampé + 0,5 s de queue (particules en vol) ; s'il reste un
        // contrôleur bouclé ou aucun contrôleur, repli 3 s.
        float life = hold > 0 ? Math.Max(hold, _fxHold ? 5f : 0f)
            : NativeFxSeconds(nif) is { } ns ? Math.Max(ns, 0.8f)
            : (_fxHold ? 5f : 3f);
        ActiveFx++;
        GD.Print($"[spellfx] natif {def.Model} x{def.Scale:0.##} sur {host.Name} ({life:0.0}s)");
        GetTree().CreateTimer(life).Timeout += () =>
        {
            ActiveFx = Math.Max(0, ActiveFx - 1);
            if (IsInstanceValid(fx)) World.SlowFree.Free(fx);
        };
        return true;
    }

    /// <summary>Durée native d'un effet non tenu : fin (StopTime,
    /// ramenée à la fréquence) du dernier contrôleur en cycle
    /// <see cref="EdenClient.World.NifCycle.Clamp"/> + 0,5 s de queue
    /// pour les particules/queues encore en vol. Null si un contrôleur
    /// boucle (effet soutenu, durée dictée par l'appelant) ou s'il n'y
    /// a aucun contrôleur.</summary>
    private static float? NativeFxSeconds(EdenClient.World.NifFile nif)
    {
        float stop = 0f; var any = false;
        foreach (var c in nif.Controllers)
        {
            if (c.Cycle == EdenClient.World.NifCycle.Loop) return null;
            var k = c.Frequency > 0f ? 1f / c.Frequency : 1f;
            var s = (c.StopTime - c.StartTime) * k + c.StartTime - c.Phase;
            if (s > stop) stop = s;
            any = true;
        }
        foreach (var sq in nif.Sequences)
        {
            if (sq.Cycle == EdenClient.World.NifCycle.Loop) return null;
            var k = sq.Frequency > 0f ? 1f / sq.Frequency : 1f;
            var s = (sq.StopTime - sq.StartTime) * k + sq.StartTime;
            if (s > stop) stop = s;
            any = true;
        }
        return any ? stop + 0.5f : null;
    }

    /// <summary>Instancie le glb d'effet sous <paramref name="host"/>.
    /// <paramref name="hold"/> &gt; 0 : l'effet vit au moins ce temps
    /// (anim rebouclée pendant le cast).</summary>
    private Node3D? Spawn(SpellFxTable.Fx? fxDef, Node3D host, float hold)
    {
        if (fxDef is not { } def || !IsInstanceValid(host)) return null;
        using var _pt = EdenClient.Perf.PerfTrace.Scope($"fx:{def.Model}");
        // Settings : « 3D Effect » (coupe tous les modèles d'effets) et
        // « Max FX » (plafond d'effets simultanés ; les siens passent
        // toujours).
        var cfg = EdenClient.Settings.SettingsApplier.Instance?.S;
        if (cfg != null)
        {
            if (!cfg.Fx3D) return null;
            if (host != _player && cfg.MaxFx < 60 && ActiveFx >= cfg.MaxFx)
                return null;
        }
        var code = def.Model;
        if (EdenClient.Native.NativeMode.Fx &&
            SpawnNative(def, host, hold, out var nat)) return nat;
        if (!AssetCatalog.TryGetGlb($"effect/model/{code}", out var abs))
        {
            GD.Print($"[spellfx] modèle absent : {code}");
            return null;
        }
        var fx = AssetCatalog.LoadGlbNode(abs);
        if (fx == null) return null;
        var layers = MakeAdditive(fx);
        host.AddChild(fx);
        fx.Position = Vector3.Zero;
        fx.Scale = Vector3.One * def.Scale;
        var ap = fx.FindChildren("*", "AnimationPlayer", true, false)
            .OfType<AnimationPlayer>().FirstOrDefault();
        float life = 1.2f;
        if (ap != null)
        {
            var name = ap.GetAnimationList().FirstOrDefault();
            if (name != null)
            {
                var anim = ap.GetAnimation(name);
                life = (float)Math.Clamp(anim.Length, 0.6, 3.0);
                if (hold > life) anim.LoopMode = Animation.LoopModeEnum.Linear;
                ap.Play(name);
            }
        }
        life = Math.Max(life, hold);
        if (_fxHold) life = Math.Max(life, 5f);   // --fx-hold : preuve visuelle
        GD.Print($"[spellfx] {code} x{def.Scale:0.##} sur {host.Name} " +
                 $"({life:0.0}s, {layers.Count} couches, " +
                 $"{layers.Count(l => l.Alpha != null)} alpha)");
        // Fondu d'entrée 0,1 s / sortie sur le dernier quart ; l'alpha
        // propre à chaque couche (contrôleurs natifs) s'y multiplie.
        var tw = fx.CreateTween();
        tw.TweenMethod(Callable.From<float>(t =>
        {
            var fade = Math.Min(t / 0.1f, 1f) *
                       Math.Clamp((life - t) / (life * 0.25f), 0f, 1f);
            foreach (var l in layers) Apply(l, t, fade);
        }), 0f, life, life);
        ActiveFx++;
        GetTree().CreateTimer(life).Timeout += () =>
        {
            ActiveFx = Math.Max(0, ActiveFx - 1);
            if (IsInstanceValid(fx)) World.SlowFree.Free(fx);
        };
        return fx;
    }
}

/// <summary>Résolution native C_Spell → effet visuel.
///   - col 4 (« E11711 ») = effet de DÉBUT de cast (flash de charge au
///     lanceur, clé <c>dynEffect.ini</c>, famille s…11) ;
///   - col 5 (« E11721 ») = effet d'incantation (boucle pendant le
///     cast, clé <c>dynEffect.ini</c>, famille s…21) ;
///   - col 6 (« s11722 ») = effet de RELEASE posé au LANCEUR au blast
///     (tornade, onde, cercle de sortie — famille s…22) ; clé
///     <c>effect.ini</c> directe ou dyn selon la ligne ;
///   - col 10 (« s11741 ») = effet d'impact sur la victime (famille
///     s…41), clé <c>effect.ini</c> directe ;
///   - <c>effect.ini</c> : col 1 = fichier nif (peut différer de la clé :
///     s90113 → s90112.nif), col 4 = échelle native (FUN_00cffc50,
///     « Effect.ini Scale error »). Ignorer cette table affichait des
///     effets à l'échelle brute du nif (jusqu'à ×6 trop grands) ou
///     le mauvais modèle.</summary>
public static class SpellFxTable
{
    /// <summary>Effet résolu : stem du glb (S&lt;chiffres&gt;) + échelle.</summary>
    public readonly record struct Fx(string Model, float Scale);

    private static Dictionary<int, (Fx? Begin, Fx? Cast, Fx? Release,
        Fx? Hit)>? _t;
    private static Dictionary<string, Fx>? _effects;
    private static Dictionary<string, string>? _dyn;
    // --fx-legacy : ancien mapping (S + chiffres, échelle 1) — preuve
    // avant/après uniquement.
    private static readonly bool _legacy = OS.GetCmdlineUserArgs()
        .Concat(OS.GetCmdlineArgs()).Contains("--fx-legacy");

    internal static IEnumerable<(int Id, Fx? Cast, Fx? Hit)> All()
    {
        if (_t == null) Load();
        return _t!.Select(kv => (kv.Key, kv.Value.Cast, kv.Value.Hit));
    }

    /// <summary>Effet de début de cast (col 4) posé au lanceur.</summary>
    public static Fx? BeginFx(int spellId) => Get(spellId).Begin;
    /// <summary>Effet d'incantation (col 5) — boucle pendant le cast.</summary>
    public static Fx? CastFx(int spellId) => Get(spellId).Cast;
    /// <summary>Effet de release (col 6) posé au LANCEUR au blast.</summary>
    public static Fx? ReleaseFx(int spellId) => Get(spellId).Release;
    /// <summary>Effet d'impact (col 10) posé sur la victime.</summary>
    public static Fx? HitFx(int spellId) => Get(spellId).Hit;

    private static (Fx? Begin, Fx? Cast, Fx? Release, Fx? Hit) Get(int id)
    {
        if (_t == null) Load();
        return _t!.TryGetValue(id, out var v) ? v : (null, null, null, null);
    }

    private static IEnumerable<string[]> IniRows(string name)
    {
        var path = AssetCatalog.ResolveRawAsset("data/db/" + name);
        if (path == null) yield break;
        foreach (var line in Vfs.EdenVfs.ReadLines(path))
        {
            var eq = line.IndexOf('=');
            if (eq < 0 || !line.StartsWith("DB")) continue;
            yield return line[(eq + 1)..].Trim().Split(',');
        }
    }

    private static string Stem(string nif)
    {
        var d = nif.LastIndexOf('.');
        return d > 0 ? nif[..d] : nif;
    }

    /// <summary>Clé effect.ini → (modèle, échelle). Clé absente de la
    /// table → modèle de même nom à l'échelle 1 (repli).</summary>
    internal static Fx? Resolve(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        key = key.Trim().ToLowerInvariant();
        if (key is "null" or "0" || key.Length < 2) return null;
        if (!_legacy && _effects != null &&
            _effects.TryGetValue(key, out var fx)) return fx;
        return new Fx(key, 1f);
    }

    /// <summary>Colonne effet de C_Spell : clé dynEffect (« e11721 »
    /// → col 3 de la ligne dyn) OU clé effect.ini directe (« s11722 »)
    /// — les deux formes coexistent selon la ligne.</summary>
    private static Fx? DynOrDirect(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var k = key.Trim().ToLowerInvariant();
        if (k is "null" or "0" || k.Length < 2) return null;
        if (k[0] == 'e' && char.IsDigit(k[1]))
            return !_legacy && _dyn != null &&
                _dyn.TryGetValue(k, out var dk)
                ? Resolve(dk) : Resolve("s" + k[1..]);
        return Resolve(k);
    }

    private static void Load()
    {
        _t = new();
        _effects = new();
        _dyn = new();
        try
        {
            foreach (var f in IniRows("effect.ini"))
            {
                if (f.Length < 5) continue;
                var scale = float.TryParse(f[4],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var sc) && sc > 0 ? sc : 1f;
                var nif = f[1].Equals("NULL", StringComparison.OrdinalIgnoreCase)
                    ? f[0] : Stem(f[1]);
                _effects[f[0].ToLowerInvariant()] =
                    new Fx(nif.ToLowerInvariant(), scale);
            }
            foreach (var f in IniRows("DynEffect.ini"))
                if (f.Length >= 3)
                    _dyn[f[0].ToLowerInvariant()] = f[2].ToLowerInvariant();
        }
        catch (Exception e)
        { GD.PrintErr($"[spellfx] effect.ini : {e.Message}"); }
        var path = AssetCatalog.ResolveRawAsset("data/db/c_spell.ini")
            ?? AssetCatalog.ResolveRawAsset("data/db/C_Spell.ini");
        if (path == null) return;
        try
        {
            foreach (var row in IniTable.ParseFile(path).Rows)
            {
                var id = IniTable.GetInt(row, 0);
                if (id <= 0) continue;
                var b = DynOrDirect(IniTable.GetStr(row, 4));   // début cast
                var c = DynOrDirect(IniTable.GetStr(row, 5));   // incantation
                var r = DynOrDirect(IniTable.GetStr(row, 6));   // release @lanceur
                var h = Resolve(IniTable.GetStr(row, 10));      // impact victime
                if (b != null || c != null || r != null || h != null)
                    _t[id] = (b, c, r, h);
            }
        }
        catch (Exception e) { GD.PrintErr($"[spellfx] c_spell : {e.Message}"); }
    }
}
