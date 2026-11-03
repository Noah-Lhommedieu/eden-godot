using System.Collections.Concurrent;
using Godot;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;
using EdenClient.World;

namespace EdenClient.Combat;

/// <summary>
/// Nombres de dégâts flottants (lot S1) — nœud monde posé sous la
/// GameScene par <see cref="Hud.HudExtensions"/>.
///
/// Sources de dégâts (lazy-bind SessionCarry, même pattern que
/// EntityRegistry) :
///
///   - <b>272</b> <see cref="ZoneClientMeleeAttack"/> — coups de
///     mêlée : texte au-dessus de la cible. Style (hitType, mot
///     drapeau natif — ZoneEntryFlow.Combat.Mob.cs serveur) :
///     8/« miss » → gris « Miss » ; bit 0x2 → jaune (crit) ; cible =
///     self → rouge ; sinon blanc ;
///   - <b>281</b> <see cref="ZoneClientSpellHitToLife"/> — impacts de
///     sorts : flags 0x200 (restore) → vert « +N » ; 0x200000 → jaune ;
///     victime = self → rouge.
///
/// Rendu : <see cref="Label3D"/> billboard au-dessus de l'entité
/// (+~2 u), montée + fade sur 1 s puis libération.
/// </summary>
public partial class DamageFloaters : Node3D
{
    /// <summary>Durée de vie d'un floater normal (s).</summary>
    [Export] public float Lifetime { get; set; } = 1.1f;
    /// <summary>Durée de vie d'un critique (s).</summary>
    [Export] public float CritLifetime { get; set; } = 1.6f;
    /// <summary>Hauteur de montée d'un floater normal (u).</summary>
    [Export] public float Rise { get; set; } = 1.4f;
    /// <summary>Hauteur de montée d'un critique (u).</summary>
    [Export] public float CritRise { get; set; } = 1.1f;

    /// <summary>Style de nombre — atlas <c>algorismNN.dds</c> natif
    /// (<c>CNumberEffect::Init</c> @0x00d71880 : algorism / miss /
    /// block / critical01 / critical02).</summary>
    private enum Kind { Damage, Taken, Heal, Crit, CritTaken, Miss, Block }

    private sealed class Floater
    {
        public Node3D Node = null!;
        public float Age, Life, Rise, BaseScale;
        public bool Crit;
        public Vector3 Start;
        public Sprite3D? Burst;
        public uint Target;
        /// <summary>Dégât REÇU (rouge) : arc + chute latérale au lieu
        /// de la montée droite des dégâts infligés (blanc).</summary>
        public bool Taken;
        public float Drift;
    }

    // Atlas chiffres : 5 colonnes × 2 rangées (0-4 / 5-9) sur 512×256.
    private const float AtlasW = 512f, CellW = 512f / 5f, CellH = 128f;
    private const float PxSize = 0.0066f;
    private static readonly Dictionary<string, Texture2D?> _tex = new();

    private readonly ConcurrentQueue<Action> _pending = new();
    private readonly List<Floater> _live = new();
    private EdenSession? _session;
    private EntityRegistry? _registry;
    private Node3D? _player;
    private bool _nodesSearched;

    /// <summary>Charge à l'avance atlas de chiffres, miss/block et aura critique : le 1er coup
    /// payait le décodage DDS sur le thread principal (~100 ms de freeze).</summary>
    public override void _Ready()
    {
        GetTree().CreateTimer(2.0).Timeout += () =>
        {
            foreach (var n in new[] { "algorism01", "algorism02", "algorism04", "algorism05", "miss", "block" })
                Tex(n);
            CritAuraTex();
        };
    }

    private static Texture2D? Tex(string name)
    {
        if (_tex.TryGetValue(name, out var t)) return t;
        Texture2D? tex = AssetCatalog.GetTexture($"effect/texture/{name}.dds");
        if (tex != null) { _tex[name] = tex; return tex; }
        _tex[name] = tex;
        return tex;
    }

    // Les textures natives critical01/02 sont des glyphes CJK — le
    // plan demande une simple aura rouge derrière le chiffre.
    private static Texture2D? _auraTex;
    private static Texture2D CritAuraTex()
    {
        if (_auraTex != null) return _auraTex;
        const int S = 128;
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        for (var y = 0; y < S; y++)
            for (var x = 0; x < S; x++)
            {
                var dx = (x - S / 2f) / (S / 2f);
                var dy = (y - S / 2f) / (S / 2f);
                var a = Mathf.Clamp(1f - Mathf.Sqrt(dx * dx + dy * dy),
                    0f, 1f);
                img.SetPixel(x, y, new Color(1f, 0.10f, 0.04f, a * a));
            }
        _auraTex = ImageTexture.CreateFromImage(img);
        return _auraTex;
    }

    public override void _Process(double delta)
    {
        if (_session == null && SessionCarry.Session is { } s)
            BindSession(s);
        if (!_nodesSearched)
        {
            _nodesSearched = true;
            _registry = GetTree().Root
                .FindChild("NetEntities", true, false) as EntityRegistry;
            _player = GetTree().Root
                .FindChild("Player", true, false) as Node3D;
        }
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex)
            {
                GD.PrintErr($"[floaters] dispatch : {ex}");
            }
        }
        FlushMeleeWait();
        var dt = (float)delta;
        var cam = GetViewport().GetCamera3D();
        for (var i = _live.Count - 1; i >= 0; i--)
        {
            var f = _live[i];
            if (!GodotObject.IsInstanceValid(f.Node))
            {
                _live.RemoveAt(i);
                continue;
            }
            f.Age += dt;
            if (f.Age >= f.Life)
            {
                f.Node.QueueFree();
                _live.RemoveAt(i);
                continue;
            }
            var t = f.Age / f.Life;
            // Montée ease-out ; le critique monte lentement après le pop.
            var rise = f.Rise * (1f - (1f - t) * (1f - t));
            if (f.Taken)
            {
                // Reçu : jaillit puis retombe en arc sur le côté
                // (≠ infligé : montée droite).
                var arc = 4f * t * (1f - t);
                var side = cam != null ? cam.GlobalBasis.X : Vector3.Right;
                f.Node.Position = f.Start + side * (f.Drift * t) +
                    new Vector3(0f, 0.75f * arc - 0.45f * t, 0f);
            }
            else
                f.Node.Position = f.Start + new Vector3(0f, rise, 0f);
            float sc;
            if (f.Crit)
            {
                // pop 0.35→1.9 (0-0.12 s), retombée 1.5 (→0.3 s), tenue.
                var a = f.Age;
                sc = a < 0.12f ? Mathf.Lerp(0.35f, 1.9f, a / 0.12f)
                    : a < 0.30f ? Mathf.Lerp(1.9f, 1.5f, (a - 0.12f) / 0.18f)
                    : 1.5f;
                if (f.Burst != null)
                {
                    f.Burst.Scale = Vector3.One * (1f + 0.35f * t);
                    f.Burst.RotationDegrees = new Vector3(0, 0, 12f * t);
                }
            }
            else
            {
                sc = f.Age < 0.1f
                    ? Mathf.Lerp(1.35f, 1f, f.Age / 0.1f) : 1f;
            }
            var fadeFrom = f.Crit ? 0.7f : 0.55f;
            var alpha = t < fadeFrom ? 1f
                : 1f - (t - fadeFrom) / (1f - fadeFrom);
            if (cam != null)
                f.Node.GlobalBasis = cam.GlobalBasis.Scaled(
                    Vector3.One * f.BaseScale * sc);
            foreach (var c in f.Node.GetChildren())
                if (c is Sprite3D sp)
                    sp.Modulate = sp.Modulate with { A = alpha };
        }
    }

    // ------------------------------------------------------------------
    // Binds session
    // ------------------------------------------------------------------

    private void BindSession(EdenSession s)
    {
        _session = s;
        s.OnZone(272, r =>
        {
            var m = new ZoneClientMeleeAttack();
            m.Deserialize(r);
            _pending.Enqueue(() => SpawnMelee(m));
        });
        s.OnZone(281, r =>
        {
            var m = new ZoneClientSpellHitToLife();
            m.Deserialize(r);
            _pending.Enqueue(() => SpawnSpellHit(m));
        });
        // 292 EffectReport : [i32 caster][i32 victime][s16 enchant]
        // [s16 0][s16 n][n×i32 valeur][i32 flags] — valeur <0 = dégât
        // (proc MultiStroke), >0 = soin ; flags = mot de hit (0x10
        // block, 0x200000 crit — même famille que 281).
        s.OnZone(292, r =>
        {
            var caster = r.ReadInt();
            var victim = r.ReadInt();
            r.ReadShort(); r.ReadShort();
            var n = r.ReadShort();
            if (n < 0 || n > 16 || r.Remaining < n * 4 + 4) return;
            var vals = new int[n];
            for (var i = 0; i < n; i++) vals[i] = r.ReadInt();
            var flags = r.ReadInt();
            _pending.Enqueue(() => SpawnEffectReport(
                unchecked((uint)victim), vals, flags));
        });
        GD.Print("[floaters] binds 272/281/292 posés");
        if (System.Environment.GetEnvironmentVariable(
                "EDEN_FLOATER_TEST") == "1")
        {
            var n = 0;
            GetTree().CreateTimer(0.3, true).Timeout += () =>
                Spawn(unchecked((uint)(SessionCarry.Character?.Id
                    ?? 0)), (137 + (n++ % 7) * 41).ToString(),
                    Kind.Crit, crit: true);
        }
    }

    /// <summary>292 — valeurs <0 dégâts, >0 soins ; crit = 0x200000.</summary>
    private void SpawnEffectReport(uint victim, int[] vals, int flags)
    {
        var self = SessionCarry.Character?.Id ?? 0;
        var crit = (flags & SpellHitConst.Crit) != 0;
        var delay = 0f;
        foreach (var v in vals)
        {
            if (v == 0) continue;
            if (v > 0)
                Spawn(victim, v.ToString(), crit ? Kind.Crit : Kind.Heal,
                    crit, delay, heal: true);
            else
                Spawn(victim, (-v).ToString(),
                    victim == self ? Kind.Taken : crit ? Kind.Crit
                    : Kind.Damage, crit, delay);
            delay += 0.18f;
        }
    }

    /// <summary>272 — dégât mêlée sur la cible. Le serveur émet DEUX 272 par coup (capture native) : le 1er porte
    /// le kfId de motion (début du swing), le « twin » (kfId −1, même dégât) part au temps <c>hit</c> de la motion
    /// (CEventDelayDamage) avec la mise à jour des PV. Le nombre ne s'affiche qu'UNE fois, sur le twin — au coup
    /// d'impact, synchro avec l'animation. Si le twin ne vient jamais (cible morte/désaggro), repli sur le 1er.
    /// Un miss (type 8) n'a pas de twin : affiché tout de suite.</summary>
    private void SpawnMelee(ZoneClientMeleeAttack m)
    {
        AttackProbe.Damage(m.Damage, m.MotionKfId);
        var miss = (m.HitType & 7) == 8 || m.HitType == 8;
        if (!AttackProbe.Legacy && !miss)
        {
            var key = (m.Attacker, m.Target);
            if (m.MotionKfId >= 0)
            {
                _meleeWait[key] = (m, Time.GetTicksMsec() + MeleeTwinWaitMs);
                return;
            }
            _meleeWait.Remove(key);
        }
        ShowMelee(m);
    }

    /// <summary>Fenêtre d'attente du twin (ms) : le natif le planifie à ~300 ms.</summary>
    private const ulong MeleeTwinWaitMs = 700;
    private readonly Dictionary<(uint, uint), (ZoneClientMeleeAttack Hit, ulong Deadline)> _meleeWait = new();

    private void FlushMeleeWait()
    {
        if (_meleeWait.Count == 0) return;
        var now = Time.GetTicksMsec();
        foreach (var (key, w) in _meleeWait.ToArray())
            if (now >= w.Deadline)
            {
                _meleeWait.Remove(key);
                ShowMelee(w.Hit);
            }
    }

    private void ShowMelee(ZoneClientMeleeAttack m)
    {
        var self = SessionCarry.Character?.Id ?? 0;
        var onSelf = m.Target == self;
        if ((m.HitType & 7) == 8 || m.HitType == 8)
        {
            Spawn(m.Target, "", Kind.Miss);
            return;
        }
        var crit = (m.HitType & 0x2) != 0;
        AttackProbe.Floater(m.Damage);
        Spawn(m.Target, m.Damage.ToString(), Style(onSelf, crit), crit);
    }

    /// <summary>281 — impact de sort : soin (vert) / dégât.</summary>
    private void SpawnSpellHit(ZoneClientSpellHitToLife m)
    {
        var self = SessionCarry.Character?.Id ?? 0;
        var onSelf = m.Target == self;
        var h = unchecked((uint)m.Target);
        var crit = (m.Flags & SpellHitConst.Crit) != 0;
        if ((m.Flags & SpellHitConst.Restore) != 0)
        {
            Spawn(h, m.Amount.ToString(), crit ? Kind.Crit : Kind.Heal,
                crit, heal: true);
            return;
        }
        if ((m.Flags & 7) == SpellHitConst.Miss)
        {
            Spawn(h, "", Kind.Miss);
            return;
        }
        if ((m.Flags & SpellHitConst.Block) != 0 && m.Amount <= 0)
        {
            Spawn(h, "", Kind.Block);
            return;
        }
        Spawn(h, m.Amount.ToString(), Style(onSelf, crit), crit);
    }

    private static Kind Style(bool onSelf, bool crit) =>
        crit ? (onSelf ? Kind.CritTaken : Kind.Crit)
            : onSelf ? Kind.Taken : Kind.Damage;

    /// <summary>Atlas de chiffres par style (natif : algorism blanc /
    /// rouge / vert / jaune).</summary>
    private static string DigitAtlas(Kind k, bool heal) =>
        heal ? "algorism02"
        : k == Kind.Taken || k == Kind.CritTaken ? "algorism04"
        : k == Kind.Crit ? "algorism05" : "algorism01";

    /// <summary>Crée le floater au-dessus de l'entité handle
    /// (registre ; si self → nœud « Player »).</summary>
    private void Spawn(uint handle, string text, Kind kind,
        bool crit = false, float delay = 0f, bool heal = false)
    {
        if (delay > 0f)
        {
            GetTree().CreateTimer(delay).Timeout += () =>
                Spawn(handle, text, kind, crit, 0f, heal);
            return;
        }
        using var _pt = EdenClient.Perf.PerfTrace.Scope($"floater:{text}");
        if (System.Environment.GetEnvironmentVariable("EDEN_FORCE_CRIT")
            == "1" && kind is Kind.Damage or Kind.Taken)
        {
            kind = kind == Kind.Taken ? Kind.CritTaken : Kind.Crit;
            crit = true;
        }
        // Prefs « Critical Shock » / « Vibration » : critique subi, ou
        // critique infligé sur la cible courante.
        if (crit && !heal && (kind == Kind.CritTaken ||
            handle == (Hud.HudExtensions.FindHud()?.Target?.TargetHandle ?? 0u)))
            EdenClient.Settings.FeedbackFx.Crit(kind == Kind.CritTaken);
        if (!Hud.HudPrefs.ShowDamage) return;
        Vector3? pos = null;
        if (_registry != null &&
            _registry.Entities.TryGetValue(handle, out var e))
            pos = e.GlobalPosition + new Vector3(0f, 2.2f, 0f);
        else if (_player != null)
        {
            var self = SessionCarry.Character?.Id ?? 0;
            if (handle == self)
                pos = _player.GlobalPosition + new Vector3(0f, 2.2f, 0f);
        }
        if (pos == null) return;

        var node = new Node3D { Name = $"dmg_{handle:x8}" };
        Sprite3D? burst = null;
        float width;
        if (kind is Kind.Miss or Kind.Block)
        {
            var tx = Tex(kind == Kind.Miss ? "miss" : "block");
            if (tx == null) { node.QueueFree(); return; }
            // Texte natif 512×256 : on ne garde que la zone utile.
            var sp = new Sprite3D
            {
                Texture = tx, PixelSize = PxSize * 0.6f,
                NoDepthTest = true, RenderPriority = 2,
                Shaded = false, TextureFilter =
                    BaseMaterial3D.TextureFilterEnum.Linear,
            };
            node.AddChild(sp);
            width = 0f;
        }
        else
        {
            var atlas = Tex(DigitAtlas(kind, heal));
            if (atlas == null) { node.QueueFree(); return; }
            var adv = CellW * PxSize * 0.82f;
            var total = text.Length;
            width = adv * total;
            for (var i = 0; i < total; i++)
            {
                var d = text[i] - '0';
                if (d < 0 || d > 9)
                {
                    // '+' etc. : ignoré (le vert suffit à marquer le soin).
                    continue;
                }
                var sp = new Sprite3D
                {
                    Texture = atlas, PixelSize = PxSize,
                    RegionEnabled = true,
                    RegionRect = new Rect2((d % 5) * CellW,
                        (d / 5) * CellH, CellW, CellH),
                    NoDepthTest = true, RenderPriority = 2, Shaded = false,
                    TextureFilter =
                        BaseMaterial3D.TextureFilterEnum.Linear,
                    Position = new Vector3(
                        -width / 2f + adv * (i + 0.5f), 0f, 0f),
                };
                node.AddChild(sp);
            }
            if (crit)
            {
                burst = new Sprite3D
                {
                    Texture = CritAuraTex(), PixelSize = 0.024f,
                    NoDepthTest = true, RenderPriority = 1,
                    Shaded = false, TextureFilter =
                        BaseMaterial3D.TextureFilterEnum.Linear,
                    Position = new Vector3(0f, 0.05f, -0.01f),
                };
                node.AddChild(burst);
            }
        }
        var taken = kind is Kind.Taken or Kind.CritTaken;
        AddChild(node);
        // Empilement : chaque floater vivant sur la même cible pousse
        // le suivant d'un cran vers le haut (hits simultanés lisibles).
        var stack = 0;
        foreach (var f in _live)
            if (f.Target == handle) stack++;
        var start = pos.Value + new Vector3(
            (float)GD.RandRange(-0.3, 0.3),
            Mathf.Min(stack, 6) * 0.4f,
            (float)GD.RandRange(-0.1, 0.1));
        node.Position = start;
        _live.Add(new Floater
        {
            Node = node, Start = start, Crit = crit, Burst = burst,
            Target = handle,
            Life = crit ? CritLifetime : Lifetime,
            Rise = crit ? CritRise : Rise,
            BaseScale = taken ? 1.25f : 1f,
            Taken = taken,
            Drift = taken ? (GD.Randf() < 0.5f ? -1f : 1f) *
                (0.6f + GD.Randf() * 0.4f) : 0f,
        });
    }
}
