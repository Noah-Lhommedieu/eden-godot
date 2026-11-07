using System.Collections.Concurrent;
using Godot;
using EdenClient.Combat;
using EdenClient.Game;
using EdenClient.Net;
using EdenClient.Player;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.World;

/// <summary>
/// Une entité réseau distante (mob, NPC, joueur, trésor) :
/// Node3D racine + visuel (GLB ou capsule placeholder) + label de nom
/// + <see cref="NetworkInterpolator"/> alimenté par les trames 326/267.
/// </summary>
public partial class RemoteEntity : Node3D
{
    /// <summary>Sight-handle réseau (charId joueur, 0x88C8xxxx NPC,
    /// 0xFFFFxxxx mob).</summary>
    public uint Handle;
    /// <summary>Template id (monsterId / npcId / 0 pour joueur).</summary>
    public int EntityId;
    /// <summary>8 = NPC, 12 = monstre, 0 = joueur/trésor (kind natif).</summary>
    public byte Kind;
    /// <summary>Nom affiché (résolution table/scene, ou DisplayName).</summary>
    public string DisplayName = "";
    /// <summary>Vrai pour un familier invoqué (entityId wire −2537,
    /// kind 12 — BuildSeeMob serveur, ZoneEntryFlow.Sight.cs).</summary>
    public bool IsElfPet;
    /// <summary>charId du maître pour un elf-pet (wire Unk48/+0x48).</summary>
    public int OwnerCharId;
    /// <summary>HP courant / max remplis par SeeMob + UpdateAttr.</summary>
    public long Hp;
    public long MaxHp;
    /// <summary>Stem du modèle GLB appliqué ("" = placeholder) —
    /// stem monstre (m025) ou lettre corps perso (g/b…) ; sert à la
    /// résolution kfId→anim de <see cref="Combat.MotionTable"/>.</summary>
    public string ModelStem = "";
    /// <summary>ClassId du 263 (KB-10) — tenue de base de SA classe quand
    /// aucune armure 4pequip n'est portée ; 0 = inconnu.</summary>
    public int ClassId;
    /// <summary>Dernière cible frappée par cette entité (272 —
    /// « cible de la cible » du TargetFrame) ; 0 = inconnue.</summary>
    public uint LastAtkTarget;
    /// <summary>Horodatage <see cref="EntityRegistry.Now"/> du dernier
    /// coup porté — la cible de la cible expire au bout de ~8 s sans
    /// hit observé.</summary>
    public double LastAtkTime;
    /// <summary>Vitesse de déplacement u/s annoncée au spawn (298
    /// FieldSpeed / 265 MoveSpeed) — horodate la destination prédite
    /// du 326. 0 = inconnue → fallback constant à la réception.</summary>
    public float MoveSpeed;
    /// <summary><see cref="EntityRegistry.Now"/> du dernier 326 reçu —
    /// le serveur cesse d'émettre dès que l'entité arrive (pas de
    /// trame d'arrêt) : passé ~0,45 s sans repush l'input « moving »
    /// de l'animator est périmé.</summary>
    public double LastMoveAt;
    /// <summary>Vitesse observée (u/s, lissée) d'une entité distante :
    /// distance entre deux 326 consécutifs / délai d'arrivée. Le client
    /// ne reçoit pas la vitesse courante (le 298/265 porte celle du
    /// spawn, périmée dès qu'on monte, qu'un buff change ou qu'un mob
    /// passe en poursuite) — elle règle la vitesse de lecture des anims
    /// de marche (<see cref="MoveAnimSpeed"/>) et borne l'horizon de
    /// prédiction (<see cref="EntityRegistry.PredictSpeed"/>).</summary>
    public float ObsSpeed;
    /// <summary>Date du dernier 267 de ruée (flag 0x6a) — poursuite
    /// connue avant que ObsSpeed ait deux mesures.</summary>
    public double ChaseAt = -100;
    public string LastMobMoveAnim = "";
    /// <summary>Dernière position (monde X/Z) et date du 326 précédent
    /// pour <see cref="ObsSpeed"/> ; date 0 = chaîne rompue (arrêt).</summary>
    public Vector2 ObsPrev;
    public double ObsPrevAt;
    /// <summary>Vrai quand l'entité a reçu sa séquence de mort
    /// (272 fatal / 305 ClientDie / HP 0) — le despawn 269 obtient
    /// alors un délai grâce pour laisser l'anim death se jouer.</summary>
    public bool Dying;

    // ---- Break Point (plan 13) ---------------------------------------
    /// <summary>S_Monster col 5 : 1 normal, 2 élite/rare, 3 boss, 4-5
    /// spéciaux — nombre d'étoiles du cadre de cible.</summary>
    public int MonsterType;
    /// <summary>S_Monster col 3 (famille) : choisit le médaillon du
    /// cadre de cible (<c>TargetHuman/Monster/UnDead/King</c>,
    /// switch natif @0x7dd57e).</summary>
    public int Family;
    /// <summary>Masque de faiblesses (c_monster col idx 10, bit attr−1).</summary>
    public int WeakMask;
    /// <summary>Patience courante (attr 0x13 du 280 ; défaut 100).</summary>
    public int Patience = 100;
    /// <summary>Patience max (champ +0x4c du 298 ; défaut 100).</summary>
    public int MaxPatience = 100;
    /// <summary>Une patience positive a déjà été reçue — distingue le
    /// 0 par défaut (mob sans jauge BP) du 0 « barre vidée ».</summary>
    public bool PatienceSeen;
    /// <summary>Mob brisé : patience &lt; 1 sur un mob à faiblesses
    /// dont la jauge a existé.</summary>
    public bool Broken { get; private set; }

    /// <summary>Apparence « brisée » native (wiki : coquille cassée,
    /// corne manquante) : le client cache les nœuds <c>KNOCK</c> et
    /// <c>KNOCKNODE</c> du NIF (FUN_0072ffe0 @0x0072ffe0 — flag
    /// « app culled » bit 0 posé sur les deux objets trouvés par nom,
    /// déclenché quand l'attr 0x13 patience tombe &lt; 1 dans
    /// FUN_00761610/FUN_00764f00/FUN_00765760, restauré à l'état 3 de
    /// FUN_00771920). 181 NIF de <c>monster/model/</c> portent un
    /// mesh rigide « KNOCK » (coquille/corne d'hermite…), 5 un
    /// « KNOCKNODE ». Remplace l'ancienne teinte violacée
    /// <c>MaterialOverlay</c> qui était une approximation inventée.</summary>
    public void SetBroken(bool on)
    {
        if (on == Broken) return;
        Broken = on;
        ApplyBrokenKnock();
        LogMobSkin("change");
    }

    /// <summary>Diag `[mobskin]` : état broken/patience + visibilité
    /// réelle des nœuds KNOCK/KNOCKNODE (spawn + changement).</summary>
    private void LogMobSkin(string why)
    {
        if (Kind != 12) return;
        var knock = "-";
        if (GetNodeOrNull<Node3D>("Visual") is { } vis)
        {
            var l = new System.Collections.Generic.List<string>();
            foreach (var n in vis.FindChildren("*", "Node3D", true, false))
            {
                var nm = n.Name.ToString();
                if (nm.StartsWith("KNOCK", StringComparison.OrdinalIgnoreCase))
                    l.Add($"{nm}={(((Node3D)n).IsVisibleInTree() ? "vis" : "hid")}");
            }
            if (l.Count > 0) knock = string.Join(",", l);
        }
        GD.Print($"[mobskin] id={EntityId} broken={Broken} " +
            $"patience={Patience}/{MaxPatience} seen={PatienceSeen} " +
            $"weak=0x{WeakMask:x} knock={knock} ({why})");
    }

    /// <summary>Applique la visibilité native des pièces « KNOCK »/
    /// « KNOCKNODE » à l'état <see cref="Broken"/> courant — rappelé
    /// par <see cref="SetVisual"/> quand le modèle arrive ou change
    /// alors que le mob est déjà brisé. Le lookup natif ne prend que
    /// la première occurrence de chaque nom ; <c>Unique()</c> de
    /// NifScene suffixe les doublons (« KNOCK2 »), donc le match
    /// exact reproduit aussi cette limite.</summary>
    private void ApplyBrokenKnock()
    {
        if (GetNodeOrNull<Node3D>("Visual") is not { } vis) return;
        var knockHidden = false;
        var knockNodeHidden = false;
        foreach (var n in vis.FindChildren("*", "Node3D", true, false))
        {
            var nm = n.Name.ToString();
            var isKnock = !knockHidden &&
                nm.Equals("KNOCK", StringComparison.OrdinalIgnoreCase);
            var isKnockNode = !isKnock && !knockNodeHidden &&
                nm.Equals("KNOCKNODE", StringComparison.OrdinalIgnoreCase);
            if (!isKnock && !isKnockNode) continue;
            if (Broken)
                ((Node3D)n).Visible = false;
            else if (!n.HasMeta("nif_hidden"))
                // Jamais vu caché d'office dans les NIF, mais on ne
                // réaffiche pas un nœud que la donnée masquait déjà.
                ((Node3D)n).Visible = true;
            knockHidden |= isKnock;
            knockNodeHidden |= isKnockNode;
            if (knockHidden && knockNodeHidden) return;
        }
    }
    /// <summary>rideKey de la monture courante (0 = à pied) — posé
    /// par <see cref="SetRide"/>.</summary>
    public int RideKey { get; private set; }
    private float _rideLift;
    private EntityAnimator? _mountAnim;

    /// <summary>Pose/retire le visuel de monture (enfant
    /// « _mount ») et hisse le visuel du perso sur la selle.
    /// rideKey=0 → démontage. Idempotent.</summary>
    public void SetRide(int rideKey)
    {
        if (rideKey == RideKey) return;
        RideKey = rideKey;
        GetNodeOrNull<Node3D>("_mount")?.QueueFree();
        _mountAnim = null;
        if (Animator != null) Animator.RideMount = null;
        var visual = GetNodeOrNull<Node3D>("Visual");
        // Pose de repos du visuel (RideSeat réécrit tout son Transform).
        if (visual != null && !visual.HasMeta("ride_rest"))
            visual.SetMeta("ride_rest", visual.Transform);
        if (rideKey <= 0)
        {
            _rideLift = 0f;
            if (visual != null)
                visual.Transform = visual.GetMeta("ride_rest").AsTransform3D();
            RelayoutOverhead();
            return;
        }
        var mount = CharVisual.BuildRideVisual(rideKey);
        if (mount == null) return;
        var holder = new Node3D { Name = "_mount" };
        // La monture partage la convention GLB « face +Z » (H9) —
        // 180°/Y sur le porteur pour aligner avec le perso retourné.
        holder.Rotation = new Vector3(0f, 0f, 0f);
        holder.AddChild(mount);
        AddChild(holder);
        // Le cavalier suit l'anim de sa monture (ride01/04-07/08).
        _mountAnim = CharVisual.MountAnimator(mount);
        if (Animator != null) Animator.RideMount = _mountAnim;
        // Selle : lookup unique partagée avec le joueur local
        // (Hud.RideFit — table par stem modèle, repli auto).
        var fit = Hud.RideFit.Apply(rideKey, holder, mount);
        _rideLift = fit.Lift;
        if (visual != null)
        {
            visual.Position = new Vector3(0f, _rideLift, fit.BackZ);
            // Natif : racine cavalier sur le nœud L17 de la monture.
            Hud.RideFit.Follow(holder, mount, visual);
        }
        RelayoutOverhead();
    }

    /// <summary>loc → itemId de l'équipement porté (conteneur 0
    /// « tenu ») — rempli par le set0 de la fiche 263
    /// (<see cref="EntityRegistry.HandleDisplayChar"/>) puis tenu à
    /// jour par les deltas 276
    /// (<see cref="EntityRegistry.HandleChangeEquip"/>). Source de
    /// <see cref="ApplyWornEquipment"/>.</summary>
    public readonly Dictionary<int, int> WornItems = new();

    /// <summary>A6.6 — (conteneur 2-4, loc 1-5) → itemId des pages
    /// costume du perso distant, alimenté par les 276 (conteneurs 2-4).
    /// Fusionné sur <see cref="WornItems"/> au rendu
    /// (<see cref="CostumeVisual.Merge"/>).</summary>
    public readonly Dictionary<(int, int), int> CostumeItems = new();

    /// <summary>(Re)applique l'équipement connu au corps perso
    /// courant — même pipeline que le joueur local
    /// (<c>EquipSlotsPanel.ApplyBodyEquipment</c> →
    /// <see cref="CharVisual.ApplyEquipment"/> : tenues 4pequip,
    /// casques/capes et armes attachés aux bones Bip01 Prop1/Prop2).
    /// No-op tant que le visuel n'est pas un corps GLB joueur
    /// (placeholder humanoïde : <see cref="ModelStem"/> vide) —
    /// les dicts restent remplis pour l'appel suivant.</summary>
    public void ApplyWornEquipment()
    {
        var visual = GetNodeOrNull<Node3D>("Visual");
        if (visual == null || Kind != 0 || ModelStem.Length != 1)
            return;
        IReadOnlyDictionary<int, int> shown = WornItems;
        if (CostumeItems.Count > 0)
            shown = CostumeVisual.Merge(WornItems,
                (c, l) => CostumeItems.TryGetValue((c, l), out var id)
                    ? id : 0,
                id => Hud.InventoryWindow.LookupDef(id)?.Kind ?? -1);
        CharVisual.ApplyEquipment(visual, ModelStem, shown,
            LookupEquipItem, ClassId, 0);
        // Casques/accessoires posés sur les os peuvent dépasser le
        // haut du corps — une re-mesure puis le cache se fige.
        InvalidateOverheadTop();
    }

    /// <summary>itemId → (Code, ModelCode) — même résolution c_item
    /// que le panneau d'équipement local
    /// (<c>EquipSlotsPanel.LookupModel</c>).</summary>
    private static (string Code, string ModelCode)? LookupEquipItem(
        int itemId)
    {
        var def = Hud.InventoryWindow.LookupDef(itemId);
        return def == null ? null : (def.Code, def.ModelCode);
    }

    /// <summary>Interpolateur réseau (alimenté par PushState).</summary>
    public NetworkInterpolator Interp { get; set; } = null!;
    /// <summary>Label flottant (nom ou id).</summary>
    public Label3D? Label { get; private set; }
    /// <summary>Animator attaché au visuel GLB courant (null si
    /// placeholder ou anims absentes).</summary>
    public EntityAnimator? Animator { get; private set; }
    /// <summary>Label agrandi (joueurs distants — plus lisibles que
    /// les tags mob/NPC).</summary>
    public bool LabelProminent;
    private MeshInstance3D? _targetRing;

    /// <summary>Cercle rouge au sol de la cible courante (ref :
    /// anneau sous le mob sélectionné). Idempotent.</summary>
    public void SetTargeted(bool on)
    {
        if (on)
        {
            // H5 : rayon d'après l'emprise réelle du modèle (les gros
            // mobs avaient un cercle minuscule), posé au sol.
            var r = 0.62f;
            if (GetNodeOrNull<Node3D>("Visual") is { } vis)
            {
                var half = 0f;
                foreach (var mi in vis.FindChildren("*", "MeshInstance3D",
                             true, false).Cast<MeshInstance3D>())
                {
                    if (mi.Mesh == null) continue;
                    var a = mi.GlobalTransform * mi.Mesh.GetAabb();
                    half = Mathf.Max(half,
                        Mathf.Max(a.Size.X, a.Size.Z) * 0.5f);
                }
                if (half > 0f) r = Mathf.Clamp(half * 0.9f, 0.5f, 5f);
            }
            if (_targetRing == null)
            {
                _targetRing = new MeshInstance3D
                {
                    Name = "TargetRing",
                    CastShadow = GeometryInstance3D
                        .ShadowCastingSetting.Off,
                };
                AddChild(_targetRing);
            }
            _targetRing.Mesh = new TorusMesh
            {
                InnerRadius = r - 0.07f, OuterRadius = r + 0.07f,
                Rings = 32, RingSegments = 8,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.9f, 0.12f, 0.12f, 0.8f),
                    ShadingMode =
                        BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency =
                        BaseMaterial3D.TransparencyEnum.Alpha,
                },
            };
            _targetRing.Position = new Vector3(0f, 0.06f, 0f);
        }
        if (_targetRing != null) _targetRing.Visible = on;
    }

    /// <summary>Joue une anim one-shot sur le visuel
    /// (attack01/hurt01…) — no-op sans animator.</summary>
    public void PlayOnce(string anim) => Animator?.PlayOnce(anim);

    /// <summary>Bulle de dialogue 3D (268 ClientSay) au-dessus du
    /// nom — remplace la précédente, disparaît seule.</summary>
    public void ShowBubble(string text) =>
        Scene.SpeechBubble3D.Show(this, text, _stackTop + 0.15f);

    /// <summary>Séquence de mort : marque l'entité, gèle la
    /// locomotion automatique (sinon le cadavre se relève en stand01
    /// quand le one-shot finit) et joue death01/die01. Idempotent.</summary>
    public void Die()
    {
        if (Dying) return;
        using var _pt = EdenClient.Perf.PerfTrace.Scope("Die()");
        EdenClient.Perf.PerfTrace.Event("Die");
        Dying = true;
        // Cadavre non sélectionnable (A1.1-e) : plus de raycast pick.
        GetNodeOrNull<Area3D>("PickArea")?.SetDeferred(
            Area3D.PropertyName.Monitorable, false);
        // Mort = plus vivant : seule l'anim de mort reste visible —
        // barre de PV, nom, titre/guilde, marque de quête disparaissent
        // tout de suite (le cadavre reste ~5 s jusqu'au 269 serveur).
        HideLivingOverlays();
        Died?.Invoke(this);
        if (Animator != null)
        {
            Animator.AutoLocomotion = false;
            Animator.PlayOnceAny("death01", "die01", "death02");
        }
    }

    /// <summary>Émis à la mort d'une entité (cible/sélection à vider).</summary>
    public static event Action<RemoteEntity>? Died;

    private void HideLivingOverlays()
    {
        if (_hpBar != null) _hpBar.Visible = false;
        if (Label != null) Label.Visible = false;
        foreach (var c in GetChildren())
            if (c is Label3D l) l.Visible = false;
    }

    /// <summary>Respawn : défait l'état mourant (filet de sécurité —
    /// un 298 peut réutiliser le handle avant le 269) et relance
    /// l'auto-locomotion stand01/move01.</summary>
    public void Revive()
    {
        if (!Dying) return;
        Dying = false;
        ApplyNamePref();
        UpdateHpBar();
        GetNodeOrNull<Area3D>("PickArea")?.SetDeferred(
            Area3D.PropertyName.Monitorable, true);
        if (Animator != null)
        {
            Animator.AutoLocomotion = true;
            Animator.PlayLoop("stand01");
        }
    }

    /// <summary>Remplace le visuel par un GLB ou la capsule de repli.
    /// <paramref name="animStem"/> = stem d'anims
    /// (« anims/monster/animation/&lt;stem&gt; », null = pas d'anims).
    /// <paramref name="skinTex"/> = texture variante npc/monstre
    /// (npclist col9, ex « m00203.dds » → monster/texture/m00203.png).
    /// Un GLB qui embarque déjà un <see cref="EntityAnimator"/>
    /// (corps perso de <see cref="CharVisual.BuildBody"/>) est utilisé
    /// tel quel — pas de re-attach.</summary>
    public void SetVisual(Node3D? glb, bool isPlayer,
        string? animStem = null, string? skinTex = null, float scale = 1f)
    {
        var old = GetNodeOrNull<Node3D>("Visual");
        if (old != null)
        {
            // RemoveChild synchrone avant le QueueFree différé :
            // sinon l'ancien nœud garde le nom « Visual » dans
            // l'arbre jusqu'à la fin de frame et AddChild
            // auto-renomme le nouveau en « @Node3D@NNNN » — tous
            // les lookups par nom (ApplyWornEquipment, SetRide…)
            // cassaient alors silencieusement.
            RemoveChild(old);
            old.QueueFree();
        }
        Node3D visual;
        if (glb != null)
        {
            visual = glb;
            visual.Name = "Visual";
            // Les GLB d'entités sont en coords natives Z-up (même
            // convention que le fsm) — l'entité vit en espace Godot
            // Y-up → rotation −90°/X + 180°/Y : les modèles convertis
            // font face +Z Godot (avant natif −Y, vérifié sur le corps
            // perso — le perso avançait dos à la marche, H9) ; le yaw
            // entité reste la convention « −Z = avant ».
            visual.RotationDegrees = new Vector3(-90f, 180f, 0f);
        }
        else
        {
            // Placeholder : humanoïde stylisé pour les joueurs (torse
            // + tête + hanches colorés — mieux qu'un cylindre seul),
            // capsule colorée par type sinon (vert = NPC, rouge = mob,
            // doré = trésor).
            var color = isPlayer
                ? new Color(0.9f, 0.5f, 0.8f)
                : Kind == 12 ? new Color(0.85f, 0.3f, 0.25f)
                : Kind == 8 ? new Color(0.3f, 0.8f, 0.4f)
                : new Color(0.9f, 0.8f, 0.3f);
            visual = isPlayer
                ? MakeHumanoidPlaceholder(color)
                : MakeCapsule(color);
        }
        // Échelle native (monsterlist col 2) posée AVANT la mesure de
        // hauteur (label/PV/zone de clic suivent la taille réelle — un
        // world boss ×3 n'a plus son nom noyé dans le corps).
        if (glb != null && scale > 0f && scale != 1f)
            visual.Scale = Vector3.One * scale;
        AddChild(visual);
        // Perso monté dont le corps vient d'être remplacé (263 de
        // refresh) : le nouveau visuel reprend la hauteur de selle.
        if (RideKey > 0 && _rideLift > 0f)
            visual.Position = new Vector3(0f, _rideLift, 0f);
        // Skin variante NPC/mob (npclist col9) — remplace l'albedo des
        // surfaces du GLB (matériaux dupliqués : le proto est partagé).
        if (skinTex is { Length: > 0 })
            ApplySkinTexture(visual, skinTex);
        // P0.4 : rayon de visibilité (~80 m) pour éviter de dessiner
        // des centaines de corps PNJ/mobs au loin sur les villes denses.
        if (glb != null)
        {
            foreach (var mi in visual
                         .FindChildren("*", "MeshInstance3D", true, false)
                         .Cast<MeshInstance3D>())
            {
                if (mi.VisibilityRangeEnd <= 0f)
                {
                    mi.VisibilityRangeEnd = 85f;
                    mi.VisibilityRangeEndMargin = 6f;
                    mi.VisibilityRangeFadeMode =
                        GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
                }
            }
        }
        // Priorité à un animator embarqué (CharVisual), sinon attach
        // des anims monstre par stem. Type C# filtré en LINQ :
        // FindChildren(typeName) ne résout que les classes natives
        // (bug documenté DragHelper.EnsureTitleBar) — sans ça
        // l'animator du corps perso n'était jamais trouvé et les
        // joueurs distants restaient sans locomotion pilotée.
        Animator = glb?
            .FindChildren("*", "", true, false)
            .OfType<EntityAnimator>().FirstOrDefault();
        if (Animator == null && glb != null && animStem is { Length: > 0 })
        {
            // Anims : anims/monster/animation/<stem>.glb — auto
            // locomotion stand01/move01 gérée par EntityAnimator.
            Animator = EntityAnimator.Attach(glb,
                $"monster/animation/{animStem}", modelKey: animStem);
        }
        Animator?.SetProcess(false);   // tick mutualisé (registry)
        if (Animator != null && Kind == 0) Animator.RideMount = _mountAnim;
        Animator?.PlayLoop(Animator.IdleAnim);
        // Labels : posés juste au-dessus du modèle (hauteur réelle
        // AABB — les petits mobs n'ont pas un tag flottant à 2 m).
        var top = VisualTopHeight(visual);
        _labelTop = top;
        // Capsule de picking (H11) : hauteur suivant le modèle réel —
        // un gros boss garde une zone cliquable généreuse.
        if (_pickShape?.Shape is CapsuleShape3D cap)
        {
            // Rayon : demi-empreinte horizontale réelle du modèle (AABB),
            // plancher 1.0 (petits mobs), plafond 0.6*hauteur ; sans ça
            // un world boss gardait une capsule r=1.0 fixe.
            var h = Mathf.Max(top + 0.3f, 1.2f);
            var r = Mathf.Clamp(VisualHalfFootprint(visual), 1.0f,
                Mathf.Max(1.0f, h * 0.6f));
            cap.Radius = r;
            cap.Height = Mathf.Max(h, r * 2f);
            _pickShape.Position = new Vector3(0f, cap.Height * 0.5f, 0f);
            if (r > 1.01f)
                GD.Print($"[pick] {Name} top={top:0.00} r={r:0.00} h={cap.Height:0.00}");
        }
        UpdateHpBar();
        // Mob déjà brisé quand le modèle (re)charge : le natif garde
        // l'état « app culled » sur les nœuds KNOCK — réapplique.
        if (Broken) ApplyBrokenKnock();
        LogMobSkin("spawn");
    }

    /// <summary>Applique la texture variante npc/mob sur les surfaces
    /// du visuel (duplique les matériaux — le proto GLB est partagé
    /// entre instances). Repli silencieux si absente.</summary>
    internal static void ApplySkinTexture(Node3D visual, string texName)
    {
        var tex = AssetCatalog.GetTexture($"monster/texture/{texName}");
        if (tex == null) return;
        // Base du skin « m01403.dds » → « m014 » : seules les surfaces
        // dont la texture d'origine a la même base reçoivent la variante
        // (corps) ; cheveux/armes/accessoires gardent la leur. Repli
        // ancien comportement (toutes) si aucune surface ne matche.
        var stem = System.IO.Path.GetFileNameWithoutExtension(texName)
            .ToLowerInvariant();
        var baseKey = stem.Length > 2 ? stem[..^2] : stem;
        var meshes = visual
            .FindChildren("*", "MeshInstance3D", true, false)
            .Cast<MeshInstance3D>().Where(m => m.Mesh != null).ToList();
        bool Targets(Material? mat, bool all) =>
            all || (mat != null && mat.HasMeta("src_tex") &&
                    mat.GetMeta("src_tex").AsString() is { Length: > 2 } t &&
                    t[..^2] == baseKey);
        var anyMatch = meshes.Any(mi =>
            Enumerable.Range(0, mi.Mesh!.GetSurfaceCount()).Any(s =>
                Targets(mi.GetSurfaceOverrideMaterial(s) ??
                        mi.Mesh.SurfaceGetMaterial(s), false)));
        // Modèle sans AUCUNE texture albedo (M115/M157 : le DDS d'origine
        // n'est pas livré dans le pack) : le skin de variante est la
        // seule texture dispo → on la pose sur les surfaces nues (sinon
        // le PNJ restait un aplat blanc/gris « sans couleur »).
        var modelHasTex = meshes.Any(mi =>
            Enumerable.Range(0, mi.Mesh!.GetSurfaceCount()).Any(s =>
                (mi.GetSurfaceOverrideMaterial(s) ??
                 mi.Mesh.SurfaceGetMaterial(s)) is { } mm &&
                (mm is ShaderMaterial ||
                 (mm is StandardMaterial3D st && st.AlbedoTexture != null))));
        foreach (var mi in meshes)
        {
            for (var s = 0; s < mi.Mesh!.GetSurfaceCount(); s++)
            {
                var mat = mi.GetSurfaceOverrideMaterial(s) ??
                          mi.Mesh.SurfaceGetMaterial(s);
                if (!Targets(mat, !anyMatch)) continue;
                if (mat is ShaderMaterial sm)
                {
                    var dup = (ShaderMaterial)sm.Duplicate();
                    dup.SetShaderParameter("albedo_tex", tex);
                    mi.SetSurfaceOverrideMaterial(s, dup);
                }
                else if (mat is StandardMaterial3D bm &&
                         (bm.AlbedoTexture != null || !modelHasTex))
                {
                    var dup = (StandardMaterial3D)bm.Duplicate();
                    dup.AlbedoTexture = tex;
                    if (!modelHasTex) dup.AlbedoColor = Colors.White;
                    mi.SetSurfaceOverrideMaterial(s, dup);
                }
            }
        }
    }

    private float _labelTop = 1.95f;
    private float _stackTop = 2.4f;
    private bool _topDirty = true;

    /// <summary>Le sommet du modèle a pu changer (échelle posée après
    /// SetVisual, équipement attaché sur les os) : la prochaine
    /// RelayoutOverhead re-mesure une fois puis le pile reste stable —
    /// re-mesurer à chaque relayout (tick 2 Hz, chaque update PV)
    /// faisait sauter les labels au rythme des AABB skinnées animées
    /// et des FX (pseudo qui « monte et descend » immobile).</summary>
    internal void InvalidateOverheadTop()
    {
        _topDirty = true;
        RelayoutOverhead();
    }

    /// <summary>Repose barre PV / valeurs / nom / guilde / titre / marque
    /// de quête au-dessus du modèle réel (AABB, monture et échelle
    /// comprises). Le sommet n'est re-mesuré que sur invalidation —
    /// sinon il est repris du cache (_labelTop, posé par SetVisual) :
    /// la pile ne bouge plus entre deux updates.</summary>
    public void RelayoutOverhead()
    {
        if (_topDirty)
        {
            _labelTop = Hud.OverheadLayout.ModelTop(this, _labelTop);
            _topDirty = false;
        }
        _stackTop = Hud.OverheadLayout.Apply(this, _labelTop);
        // Le blob Decal sous l'entité ne doit pas se projeter sur le
        // modèle (couche 2 — couvre aussi monture/équipement ajoutés
        // après coup).
        BlobShadow.ExemptVisuals(this);
    }
    private CollisionShape3D? _pickShape;

    /// <summary>Bit de collision layer pour la capsule de picking
    /// entité (raycast sélection — PlayerController.PickEntity).
    /// Couche 5 (valeur 16) — distincte du monde (1) et du joueur.</summary>
    public const uint PickLayerMask = 1u << 4;

    /// <summary>Capsule de sélection élargie (clic droit NPC/mob —
    /// le mesh précis était trop dur à viser, H11) : Area3D enfant
    /// « PickArea » + CapsuleShape3D r≈1.0, hauteur = haut du visuel.
    /// </summary>
    public override void _Ready()
    {
        var area = new Area3D
        {
            Name = "PickArea",
            CollisionLayer = PickLayerMask,
            CollisionMask = 0,
            Monitoring = false,
            Monitorable = true,
        };
        area.SetMeta("pick_entity", this);
        _pickShape = new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 1.0f, Height = 1.8f },
            Position = new Vector3(0f, 0.9f, 0f),
        };
        area.AddChild(_pickShape);
        AddChild(area);
    }

    /// <summary>Hauteur du haut du visuel dans le repère entité
    /// (union des AABB des meshes visibles — transforms LOCAUX
    /// composés jusqu'à <paramref name="visual"/>, donc valable avant
    /// que l'entité soit dans l'arbre ; repli ~1.9 m).</summary>
    /// <summary>Demi-empreinte horizontale (max des demi-étendues X/Z
    /// de l'union des AABB, repère visual, échelle incluse).</summary>
    internal static float VisualHalfFootprint(Node3D visual)
    {
        float minX = float.MaxValue, maxX = float.MinValue,
              minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var mi in visual
                     .FindChildren("*", "MeshInstance3D", true, false)
                     .Cast<MeshInstance3D>())
        {
            if (!mi.Visible || mi.Mesh == null) continue;
            var rel = mi.Transform;
            for (var p = mi.GetParent(); p != visual &&
                 p is Node3D n3; p = p.GetParent())
                rel = n3.Transform * rel;
            rel = visual.Transform * rel;
            var aabb = mi.GetAabb();
            for (var i = 0; i < 8; i++)
            {
                var c = aabb.Position + new Vector3(
                    (i & 1) == 0 ? 0f : aabb.Size.X,
                    (i & 2) == 0 ? 0f : aabb.Size.Y,
                    (i & 4) == 0 ? 0f : aabb.Size.Z);
                var q = rel * c;
                minX = Mathf.Min(minX, q.X); maxX = Mathf.Max(maxX, q.X);
                minZ = Mathf.Min(minZ, q.Z); maxZ = Mathf.Max(maxZ, q.Z);
            }
        }
        return maxX < minX ? 0f : Mathf.Max(maxX - minX, maxZ - minZ) * 0.5f;
    }

    internal static float VisualTopHeight(Node3D visual)
    {
        var top = float.MinValue;
        foreach (var mi in visual
                     .FindChildren("*", "MeshInstance3D", true, false)
                     .Cast<MeshInstance3D>())
        {
            if (!mi.Visible || mi.Mesh == null) continue;
            // Transform mi → repère visual (produit des parents).
            var rel = mi.Transform;
            for (var p = mi.GetParent(); p != visual &&
                 p is Node3D n3; p = p.GetParent())
                rel = n3.Transform * rel;
            rel = visual.Transform * rel;
            var aabb = mi.GetAabb();
            for (var i = 0; i < 8; i++)
            {
                var c = aabb.Position + new Vector3(
                    (i & 1) == 0 ? 0f : aabb.Size.X,
                    (i & 2) == 0 ? 0f : aabb.Size.Y,
                    (i & 4) == 0 ? 0f : aabb.Size.Z);
                var p = rel * c;
                if (p.Y > top) top = p.Y;
            }
        }
        return top > -900f ? top : 1.9f;
    }

    /// <summary>Applique le visuel d'un coffre au sol (A6.3) : GLB
    /// <c>item/model/&lt;TypeCode&gt;</c> normalisé ~0.8 u de haut, ou
    /// capsule 0.35 u teintée (repli) + halo billboard additif teinté
    /// par la qualité. Idempotent (re-SeeTreasure).</summary>
    public void SetTreasureVisual(Node3D? glb, Color quality,
        string typeCode = "")
    {
        GetNodeOrNull<Node3D>("Halo")?.QueueFree();
        TreasureTint = quality;
        float top;
        if (glb != null)
        {
            glb.RotationDegrees = new Vector3(-90f, 180f, 0f);
            var h = VisualTopHeight(glb);
            var k = EdenClient.Data.TreasureRules.ScaleFor(h);
            if (k != 1f) glb.Scale = Vector3.One * k;
            SetVisual(glb, isPlayer: false);
            top = VisualTopHeight(GetNode<Node3D>("Visual"));
        }
        else
        {
            SetVisual(null, isPlayer: false);
            var vis = GetNode<Node3D>("Visual");
            vis.Scale = Vector3.One * 0.35f;
            if (vis is MeshInstance3D mi && mi.Mesh is CapsuleMesh cm)
                cm.Material = new StandardMaterial3D
                {
                    AlbedoColor = quality,
                    EmissionEnabled = true,
                    Emission = quality,
                    EmissionEnergyMultiplier = 0.6f,
                };
            vis.Position = new Vector3(0f, 0.3f, 0f);
            top = 0.6f;
        }
        // Halo : vraie texture item/texture/<TypeCode>5x (G00006 →
        // G0000651, RGBA 128², centre blanc) si présente ; sinon
        // dégradé radial procédural.
        Texture2D? haloTex = null;
        if (typeCode.Length > 0)
            foreach (var sfx in new[] { "51", "52", "53", "54", "55", "56" })
                if ((haloTex = AssetCatalog.GetTexture(
                        $"item/texture/{typeCode}{sfx}.dds")) != null)
                    break;
        var halo = new MeshInstance3D
        {
            Name = "Halo",
            Mesh = new QuadMesh { Size = new Vector2(2.2f, 2.2f) },
            Position = new Vector3(0f, Mathf.Max(top, 0.5f) * 0.5f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeEnd = 40f,
        };
        _haloTexShader ??= new Shader { Code = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled;
uniform vec4 tint : source_color = vec4(1.0);
uniform sampler2D halo_tex : source_color, filter_linear;
void vertex() {
    MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
}
void fragment() {
    vec4 t = texture(halo_tex, UV);
    ALBEDO = tint.rgb * t.rgb * t.a * 0.8;
    ALPHA = 1.0;
}" };
        _haloShader ??= new Shader { Code = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled;
uniform vec4 tint : source_color = vec4(1.0);
void vertex() {
    MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
}
void fragment() {
    float d = length(UV - vec2(0.5)) * 2.0;
    float a = clamp(1.0 - d, 0.0, 1.0);
    a = a * a * 0.55;
    ALBEDO = tint.rgb * a;
    ALPHA = 1.0;
}" };
        var mat = new ShaderMaterial
        {
            Shader = haloTex != null ? _haloTexShader : _haloShader,
        };
        mat.SetShaderParameter("tint", quality);
        if (haloTex != null) mat.SetShaderParameter("halo_tex", haloTex);
        halo.MaterialOverride = mat;
        AddChild(halo);
        // Le quad halo (billboard face caméra) est ajouté APRÈS la
        // passe d'exemption de SetVisual : laissé couche 1, le Decal
        // d'ombre sous l'entité s'y projetait → tache sombre dépendant
        // de l'angle de vue (même fix D4 que les toiles alpha mobs).
        BlobShadow.ExemptVisuals(this);
    }
    private static Shader? _haloShader, _haloTexShader;

    /// <summary>Capsule colorée — placeholder NPC/mob/trésor.</summary>
    private static MeshInstance3D MakeCapsule(Color color)
    {
        var mi = new MeshInstance3D
        {
            Name = "Visual",
            Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.7f },
            Position = new Vector3(0f, 0.85f, 0f),
        };
        ((CapsuleMesh)mi.Mesh).Material = new StandardMaterial3D
        {
            AlbedoColor = color,
        };
        return mi;
    }

    /// <summary>Humanoïde stylisé (3 primitives empilées : hanches,
    /// torse, tête) — placeholder des joueurs distants tant que le
    /// blob d'apparence (263) n'a pas fourni le vrai corps GLB.</summary>
    private static Node3D MakeHumanoidPlaceholder(Color color)
    {
        var root = new Node3D { Name = "Visual" };
        var mat = new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = 1f,
        };
        var cloth = new StandardMaterial3D
        {
            AlbedoColor = new Color(color.R * 0.55f, color.G * 0.55f,
                color.B * 0.55f),
            Roughness = 1f,
        };
        void Add(PrimitiveMesh mesh, float y, Material m)
        {
            mesh.Material = m;
            root.AddChild(new MeshInstance3D
            {
                Mesh = mesh, Position = new Vector3(0f, y, 0f),
            });
        }
        Add(new CapsuleMesh { Radius = 0.26f, Height = 0.8f }, 0.4f, cloth);
        Add(new CapsuleMesh { Radius = 0.32f, Height = 1.0f }, 1.05f, mat);
        Add(new SphereMesh { Radius = 0.22f, Height = 0.45f }, 1.72f, mat);
        return root;
    }

    /// <summary>Couleur du label par type d'entité — convention
    /// native : NPC jaune-vert, mob blanc (hostile → rouge),
    /// joueur cyan, trésor doré.</summary>
    public Color? TreasureTint { get; set; }

    public Color LabelColor()
    {
        if (LabelProminent) return new Color(0.55f, 0.95f, 1f);
        if (TreasureTint is { } tint) return tint;
        return Kind switch
        {
            8 => new Color(0.75f, 1f, 0.45f),   // NPC — vert-jaune
            // mob — danger relatif (2.5) : rouge aggro / jaune clair
            // non agressif / gris trop bas niveau ; blanc si inconnu.
            12 => IsElfPet ? new Color(1f, 1f, 1f)
                : MobDanger.ColorOf(EntityId & 0xFFFF)
                  ?? new Color(1f, 1f, 1f),
            _ => new Color(1f, 0.85f, 0.4f),    // trésor — doré
        };
    }

    /// <summary>Recolore le label (changement de niveau joueur).</summary>
    public void RefreshLabelColor()
    {
        if (Label != null) Label.Modulate = LabelColor();
    }

    /// <summary>Crée/met à jour le label 3D flottant.</summary>
    public void SetLabel(string text)
    {
        var prominent = LabelProminent;
        if (Label == null)
        {
            Label = new Label3D
            {
                Name = "Label",
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Position = new Vector3(0f, 2.1f, 0f),
                OutlineModulate = new Color(0f, 0f, 0f, 0.85f),
                // Perf : hors de portée de lecture (~90 u) le label ne
                // sert plus — il coûte 1 draw + un raster par frame.
                VisibilityRangeEnd = 96f,
            };
        }
        Label.FontSize = prominent ? 40 : 28;
        Label.PixelSize = prominent ? 0.006f : 0.009f;
        Label.OutlineSize = prominent ? 10 : 8;
        Label.Modulate = LabelColor();
        if (Label.GetParent() == null) AddChild(Label);
        Label.Text = text;
        ApplyNamePref();
    }

    /// <summary>Préférences « Name » / « NPC Name » : joueurs distants
    /// vs PNJ+monstres.</summary>
    private void ApplyNamePref()
    {
        if (Label == null) return;
        Label.Visible = !Dying && (Kind == 0 ? Hud.HudPrefs.ShowNames
                                  : Hud.HudPrefs.ShowNpcNames);
        RelayoutOverhead();
    }

    public override void _EnterTree() => Hud.HudPrefs.Changed += ApplyNamePref;
    public override void _ExitTree() => Hud.HudPrefs.Changed -= ApplyNamePref;

    // ---- barre HP sous le nom (mobs / autres joueurs) ----
    // UN seul quad billboard à shader (HpBar3D) : l'ancien couple
    // fond+remplissage coplanaires changeait d'ordre de profondeur
    // selon le côté de la caméra (vide vue du nord, pleine vue du sud).
    private Hud.HpBar3D? _hpBar;

    /// <summary>Met à jour la barre HP (mobs kind 12 et joueurs
    /// distants kind 0 ; visibilité réglée par les Préférences).</summary>
    public void UpdateHpBar()
    {
        var want = (Kind == 12 || Kind == 0) && MaxHp > 0 && !Dying;
        if (!want)
        {
            _hpBar?.SetHp(0, 0, false);   // masque la barre
            Hud.OverheadLayout.SetValues(this, "");
            RelayoutOverhead();
            return;
        }
        if (_hpBar == null)
        {
            _hpBar = new Hud.HpBar3D(Kind == 12
                ? Hud.HudPrefs.BarRole.Mob : Hud.HudPrefs.BarRole.Other,
                new Vector2(1.1f, 0.12f));
            if (Kind != 12) _hpBar.FixedFill = new Color(0.85f, 0.15f, 0.2f);
            AddChild(_hpBar);
        }
        _hpBar.SetHp(Hp, MaxHp);
        Hud.OverheadLayout.SetValues(this, Hud.OverheadLayout.ValuesText(
            Hud.HudPrefs.OverheadValues, true, Hp, MaxHp, false, 0, 0));
        RelayoutOverhead();
    }
}

/// <summary>
/// Registre des entités réseau de la zone courante :
/// sight-handle → <see cref="RemoteEntity"/>.
///
/// Branché par <see cref="GameScene"/> sur les NCIDs zone via
/// <c>EdenSession.OnZone</c>. Toutes les méthodes <c>Handle*</c>
/// doivent être appelées sur le thread principal Godot (la session
/// ré-émet sur le thread réseau — GameScene reposte via une file +
/// <c>_Process</c>).
///
/// Positions réseau : coords Eden (x, y sol, z hauteur) converties via
/// <see cref="WorldConv.ToGodot"/>. Hauteur : quand le paquet ne porte
/// pas de z, <see cref="GroundHeight"/> snap le terrain.
/// </summary>
public partial class EntityRegistry : Node3D
{
    private readonly Dictionary<uint, RemoteEntity> _entities = new();

    /// <summary>Compte d'entités vivantes (debug/HUD).</summary>
    public int Count => _entities.Count;

    /// <summary>Hauteur du sol (godotX, godotZ) → y — injectée depuis
    /// MapLoader (TerrainSampler).</summary>
    public Func<float, float, float?>? GroundHeight;

    /// <summary>Sol physique (godotX, godotZ, refY, terrainY) → y,
    /// évalué en frame physique — cartes sans heightfield (donjons à
    /// modèle unique) où le z serveur n'est qu'une estimation de
    /// scène, et vérif des sols OBJ (ponts/dalles) au-dessus du
    /// terrain. terrainY = NaN quand la case n'a pas de heightfield :
    /// le résultat n'a alors aucun plancher minimum.</summary>
    public Func<float, float, float, float, float?>? PhysicsGround;

    /// <summary>Référence verticale des requêtes de sol physiques : Y du
    /// joueur local. Sur un donjon multi-étages les entités en vue
    /// partagent presque toujours son étage ; sans elle le rayon part
    /// de 0 et peut choisir l'étage du bas.</summary>
    public Func<float>? RefGroundY;
    private readonly Dictionary<long, float> _groundCache = new();
    private readonly Queue<(long Key, float X, float Z, float RefY,
        float TerrainY)> _groundQueue = new();
    private readonly HashSet<long> _groundPending = new();
    private static long GroundKey(float x, float z) =>
        ((long)MathF.Round(x * 2f) << 32) ^ (uint)(int)MathF.Round(z * 2f);

    /// <summary>Résout les requêtes de sol en attente (frame physique) et
    /// recale les entités déjà posées sur la case.</summary>
    public override void _PhysicsProcess(double delta)
    {
        if (PhysicsGround == null) return;
        for (var n = 0; n < 48 && _groundQueue.Count > 0; n++)
        {
            var (key, x, z, refY, terrainY) = _groundQueue.Dequeue();
            _groundPending.Remove(key);
            if (PhysicsGround(x, z, refY, terrainY) is not { } gy)
                continue;
            _groundCache[key] = gy;
            foreach (var e in _entities.Values)
            {
                // Correction poussée dans le buffer — écrire Position
                // ici serait écrasé la frame suivante par Interp.Tick.
                var p = e.Position;
                // Entité en marche (326 < 0,6 s) : un Push(Now, position
                // rendue d'il y a 0,15 s) casserait le segment cur→dst
                // (à-coup/recul) ; la case résolue sert au 326 suivant.
                if (e.Interp != null && Now - e.LastMoveAt > 0.6 &&
                    Mathf.Abs(p.X - x) < 0.8f &&
                    Mathf.Abs(p.Z - z) < 0.8f &&
                    Mathf.Abs(p.Y - gy) > 0.05f)
                    e.Interp.Buffer.Push(Now, p.X, gy, p.Z, e.Rotation.Y);
            }
        }
    }

    /// <summary>Résolution modèle+nom d'un mob par monsterId —
    /// injectée par GameScene (Scene.reborn_monsters → c_monster.ini).
    /// Retourne (stem modèle, nom, niveau) ou null.</summary>
    public Func<int, (string Model, string Name, int Level)?>?
        ResolveMonster;

    /// <summary>Résolution modèle+nom d'un NPC par npcId —
    /// (Scene.npcs). Retourne (stem modèle, nom) ou null.</summary>
    public Func<int, (string Model, string Name)?>? ResolveNpc;

    /// <summary>Résolution modèle+nom+niveau d'un elf-pet par
    /// ownerCharId (entityId −2537 — le wire ne porte pas le
    /// template ; le bridge elf le déduit du familier invoqué du
    /// self). Retourne (stem modèle, nom, niveau) ou null.</summary>
    public Func<int, (string Model, string Name, int Level)?>?
        ResolveElfPet;

    /// <summary>Évènement spawn (handle, entityId, kind) — stats/log.</summary>
    public event Action<uint, int, byte>? Spawned;
    /// <summary>Évènement despawn (handle).</summary>
    public event Action<uint>? Despawned;
    /// <summary>Évènement position self reçue (264) — x, y Eden.</summary>
    public event Action<float, float>? SelfMoved;

    /// <summary>Chaque entité connue (itération debug).</summary>
    public IReadOnlyDictionary<uint, RemoteEntity> Entities => _entities;

    /// <summary>Grâce accordée à un cadavre lors d'un despawn 269
    /// pendant que l'anim death se joue (le serveur natif retarde
    /// déjà le 269 de ~5 s — cette marge ne couvre que la fin
    /// d'animation).</summary>
    [Export] public float CorpseGraceSec { get; set; } = 2.0f;

    // ---- combat / mort / apparence (bindings session paresseux) ----
    private readonly ConcurrentQueue<Action> _netPending = new();
    private readonly List<(RemoteEntity Entity, double FreeAt)> _corpses =
        new();
    private readonly Dictionary<uint, byte> _whoisSent = new();
    private readonly HashSet<uint> _whoisWanted = new();
    private EdenSession? _session;
    private PlayerController? _player;
    private uint _selfCharId;
    /// <summary>charId du joueur local (posé à l'entrée de zone —
    /// sert aux paquets « complétude » qui visent le self).</summary>
    public uint SelfCharId => _selfCharId;

    private static double Now => Time.GetTicksUsec() / 1_000_000.0;

    /// <summary>File de construction différée des visuels GLB (P0.4 —
    /// bornée en ms/frame dans <see cref="_Process"/> pour ne pas
    /// bloquer la boucle principale sur un burst de 263 SeeMob).</summary>
    private readonly Queue<(uint Handle, string Stem, string Skin,
        float Scale, double QueuedAt)> _pendingVisuals = new();

    /// <summary>Réchauffage fond des ressources d'un modèle de mob —
    /// proto GLB + texture variante + fichiers .kf (P0.5). À froid le
    /// <see cref="RemoteEntity.SetVisual"/> d'un nouveau stem coûte
    /// 18-37 ms de parse dans la frame (mesuré --perf : grind =
    /// respawns → pics à chaque mob entrant en vue). Le warm démarre
    /// dès le 298 ; <see cref="DrainPendingVisuals"/> ne construit le
    /// visuel qu'une fois les trois ressources chaudes (filet ~1,5 s
    /// si la tâche traîne). 0 = en cours, 1 = prêt (ou raté — repli
    /// synchrone).</summary>
    private static readonly ConcurrentDictionary<string, byte>
        s_mobWarm = new(StringComparer.OrdinalIgnoreCase);

    private static bool IsMobModelWarm(string stem, string skin) =>
        s_mobWarm.TryGetValue(stem + "|" + skin, out var s) && s == 1;

    private static void WarmMobModel(string stem, string skin)
    {
        if (stem.Length == 0) return;
        if (!s_mobWarm.TryAdd(stem + "|" + skin, 0)) return;
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                if (AssetCatalog.TryGetGlb($"monster/model/{stem}",
                        out var gp))
                    AssetCatalog.GetProto(gp);
                if (skin.Length > 0)
                    AssetCatalog.GetTexture($"monster/texture/{skin}");
                EntityAnimator.PreloadAnimFiles(
                    new[] { $"monster/animation/{stem}" });
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[mobwarm] {stem} : {ex.Message}");
            }
            s_mobWarm[stem + "|" + skin] = 1;
        });
    }

    /// <summary>Construit les visuels GLB en attente dans un budget de
    /// <paramref name="maxMs"/> ms par frame (triés du plus proche du
    /// joueur au plus loin). Une entrée dont le modèle n'est pas
    /// réchauffé est repoussée en queue au lieu de payer le parse à
    /// froid dans la frame.</summary>
    private void DrainPendingVisuals(double maxMs = 4.0)
    {
        if (_pendingVisuals.Count == 0) return;
        var t0 = Time.GetTicksUsec();
        var budgetUs = (ulong)(maxMs * 1000.0);
        var built = 0;
        // Un passage borne sur le contenu présent : les entrées froides
        // repartent en queue sans bloquer les suivantes.
        var scanned = _pendingVisuals.Count;
        while (_pendingVisuals.Count > 0 && built < 4 && scanned-- > 0)
        {
            var (handle, stem, skin, scale, queuedAt) =
                _pendingVisuals.Dequeue();
            if (!IsMobModelWarm(stem, skin) && Now - queuedAt < 1.5)
            {
                _pendingVisuals.Enqueue(
                    (handle, stem, skin, scale, queuedAt));
                continue;
            }
            if (!_entities.TryGetValue(handle, out var e) ||
                !GodotObject.IsInstanceValid(e) ||
                e.ModelStem != stem)
                continue;
            e.SetVisual(LoadMobGlb(stem), isPlayer: false,
                animStem: stem, skinTex: skin);
            if (scale > 0f && scale != 1f &&
                e.GetNodeOrNull<Node3D>("Visual") is { } vis)
                vis.Scale = Vector3.One * scale;
            e.InvalidateOverheadTop();
            built++;
            if (Time.GetTicksUsec() - t0 >= budgetUs) break;
        }
    }

    // ------------------------------------------------------------------
    // Handlers par message (appelés depuis GameScene, thread principal)

    /// <summary>298 SeeMob — spawn/mise à jour d'un mob ou NPC.
    /// Positions (x, y) sol Eden ; FieldZ natif = direction (le
    /// serveur C# y écrit e.Z pour ses spawns) ; FieldSpeed natif =
    /// vitesse (le serveur C# y écrit facing pour les mobs).</summary>
    public void HandleSeeMob(ZoneSeeMob m)
    {
        var e = GetOrCreate(m.Handle, m.EntityId, m.Kind);
        e.Revive();
        // Statue de hall : les i64 hp/maxHp du 298 ne sont PAS la vie
        // — NotifyStatue y packe les équipements du clone (paires
        // u16, BornStatue natif). La barre reste sur la valeur
        // précédente (0 → masquée) ; les 280 attr la corrigent.
        if ((m.EntityId & 0xFFFF) is not (>= 54901 and <= 54905))
        {
            e.Hp = m.Hp;
            e.MaxHp = m.MaxHp;
        }
        e.UpdateHpBar();
        if (m.Kind == 12 && m.EntityId != -2537)
        {
            // Break Point : max = champ +0x4c du 298 (CMonster::OnSeen
            // passe MaxPatience), faiblesses/type depuis c_monster.
            var md = MobDanger.Lookup?.Invoke(m.EntityId & 0xFFFF);
            e.MonsterType = md?.MonsterType ?? 0;
            e.Family = md?.Family ?? 0;
            e.WeakMask = md?.BpWeakMask ?? 0;
            e.MaxPatience = m.Unk4C > 0 ? m.Unk4C : 100;
            e.Patience = e.MaxPatience;   // le 280 attr 0x13 corrige ensuite
            e.SetBroken(false);
        }

        // Résolution nom + modèle selon le kind.
        var modelStem = "";
        var skinTex = "";
        var label = m.DisplayName;
        // Familier invoqué : le serveur envoie les elf-pets en
        // monstres kind 12 avec entityId fixe −2537, le nom du pet
        // en DisplayName et le charId du maître en Unk48
        // (BuildSeeMob — ZoneEntryFlow.Sight.cs). Le template réel
        // n'est pas sur le fil : il est déduit via ResolveElfPet
        // (livre d'elfs du self → C_Partner.ini).
        if (m.EntityId == -2537)
        {
            e.IsElfPet = true;
            e.OwnerCharId = m.Unk48;
            var info = ResolveElfPet?.Invoke(m.Unk48);
            if (info is { } ei)
            {
                modelStem = ei.Model;
                if (label.Length == 0) label = ei.Name;
            }
            if (label.Length == 0) label = "Familiar";
        }
        // Statue de hall de famille (kind 12, EntityId 54901-54905 —
        // désasm client @0x70ce18) : le DisplayName est le CSV natif
        // de BornStatue « nom,genre,face,hair,weapon,param,classe,
        // style,hide » (FamilyHallRules.BuildStatueCsv). Le corps est
        // le même pipeline joueur que le 263 — le champ « genre » du
        // CSV EST le body code (gender_id DB = nibble haut d'app[28],
        // AppearanceBlob.Decode : 0=g fille, 1=b garçon, 2=zumi…) ;
        // les couleurs ne sont pas sur le fil → défauts du blob.
        else if ((m.EntityId & 0xFFFF) is >= 54901 and <= 54905)
        {
            var f = m.DisplayName.Split(',');
            if (f.Length > 0) label = f[0];
            if (f.Length >= 9 &&
                int.TryParse(f[1], out var gender) &&
                int.TryParse(f[2], out var face) &&
                int.TryParse(f[3], out var hair) &&
                int.TryParse(f[6], out var classId) &&
                int.TryParse(f[7], out var style))
            {
                var app = EdenClient.Protocol.AppearanceCodec.Encode(
                    new(BodyCode: gender, HairStyle: hair,
                        FaceStyle: face, HairColor: 0, SkinColor: 0,
                        EyesColor: 0, EyesColor2: 0));
                var body = CharVisual.BuildBody(app,
                    classId: classId, style: style);
                // Équipements du clone — paires u16 packées dans les
                // champs optionnels du 298 (NotifyStatue natif) :
                // hp=tête|torse, maxHp=jambes|gants, +0x34=pieds|dos,
                // +0x38=droite|gauche, +0x48=distance|action.
                var worn = new Dictionary<int, int>();
                void EquipAt(int loc, long packed, int shift)
                {
                    var id = (int)(packed >> shift) & 0xFFFF;
                    if (id != 0) worn[loc] = id;
                }
                EquipAt(1, m.Hp, 0); EquipAt(2, m.Hp, 16);
                EquipAt(3, m.MaxHp, 0); EquipAt(4, m.MaxHp, 16);
                EquipAt(5, m.Unk34, 0); EquipAt(6, m.Unk34, 16);
                EquipAt(7, m.Unk38, 0); EquipAt(8, m.Unk38, 16);
                EquipAt(10, m.Unk48, 0);
                GD.Print($"[registry] statue #{m.EntityId & 0xFFFF} " +
                    $"label=«{label}» body={(body != null ? "ok" : "NULL")} " +
                    $"équip={worn.Count} pos=({m.X:F1},{m.Y:F1})");
                if (body != null)
                {
                    e.SetVisual(body, isPlayer: true);
                    e.ModelStem = CharModelSpec.BodyStem(app);
                    if (worn.Count > 0)
                        CharVisual.ApplyEquipment(body, e.ModelStem, worn,
                            id => Hud.InventoryWindow.LookupDef(id) is { } d
                                ? (d.Code, d.ModelCode) : ((string, string)?)null,
                            classId, style);
                    // Gestuelle figée du clone — action_id natif (high16
                    // d'Unk48/Unk44) résolu via motion.ini sur le stem du
                    // corps ; 0 = idle courant (pas de gel). Le client
                    // natif active la séquence KF figée (CStatue::Init
                    // @0x775f00 → FUN_00ad4300/FUN_00ad4060).
                    var actionId = (int)(m.Unk48 >> 16) & 0xFFFF;
                    if (actionId != 0 &&
                        Combat.MotionTable.Shared.Lookup(
                            e.ModelStem, actionId) is { } pose &&
                        body.FindChildren("*", "Node", true, false)
                            .OfType<EntityAnimator>()
                            .FirstOrDefault() is { } stAnim)
                        stAnim.Freeze(pose);
                    if (OS.GetCmdlineUserArgs().Contains("--look-log"))
                        CharVisual.DumpVisible(body,
                            $"statue#{m.EntityId & 0xFFFF}");
                }
            }
        }
        else if (m.Kind == 12)
        {
            var info = ResolveMonster?.Invoke(m.EntityId & 0xFFFF);
            if (info is { } mi)
            {
                modelStem = mi.Model;
                // t_monster.ini EN prioritaire — le DisplayName
                // serveur est Big5 et réécrirait le label à chaque
                // re-SeeMob (le hook Spawned ne corrige que la 1re
                // apparition).
                var en = EdenClient.Data.TextResolver.MonsterName(
                    m.EntityId & 0xFFFF);
                if (en != null) label = en;
                else if (label.Length == 0) label = mi.Name;
                label = EdenClient.Data.BreakPointUi.FormatMobLabel(
                    mi.Level, label);
            }
        }
        var npcScale = 1f;
        if (m.Kind == 12 && m.EntityId != -2537 && !e.IsElfPet &&
            System.Environment.GetEnvironmentVariable("EDEN_MOB_SCALE") != "0")
        {
            // Taille + skin NATIFS : monsterlist.ini (col 2 échelle, col 8
            // texture) indexé par le code modèle c_monster. Les variantes
            // d'un même NIF (3 couleurs de champignoïde, version « world
            // boss » 2-4× plus grande…) y sont distinguées ; sans cette
            // table tous les mobs d'un modèle partageaient texture et taille.
            var sd = MobDanger.Lookup?.Invoke(m.EntityId & 0xFFFF);
            if (sd != null && MonsterModelTable.Resolve(sd.ModelCode) is { } mm)
            {
                npcScale = mm.Scale;
                skinTex = mm.Texture;
            }
            else if (sd is { IsStationary: false })
                // Code absent de monsterlist (typos des ini) : repli
                // HYPOTHESIS historique.
                npcScale = EdenClient.Data.BreakPointUi.MobScale(
                    sd.MonsterType, m.Handle);
        }
        if (m.Kind == 8)
        {
            // Table c_npc+npclist d'abord (stem + texture variante
            // fidèles — le « motion » de la scène n'est qu'une anim,
            // symptôme H4) ; la scène sert de repli.
            if (NpcModelTable.Resolve(m.EntityId & 0xFFFF) is { } nd)
            {
                modelStem = nd.Stem;
                skinTex = nd.Texture;
                if (nd.Scale > 0f) npcScale = nd.Scale;
            }
            var info = ResolveNpc?.Invoke(m.EntityId & 0xFFFF);
            if (info is { } ni)
            {
                if (modelStem.Length == 0) modelStem = ni.Model;
                if (label.Length == 0) label = ni.Name;
            }
            // t_npc.ini EN prioritaire (même raison que kind 12).
            var en = EdenClient.Data.TextResolver.NpcName(
                m.EntityId & 0xFFFF);
            if (en != null)
            {
                var title = EdenClient.Data.TextResolver.NpcTitle(
                    m.EntityId & 0xFFFF);
                label = title != null ? $"<{title}>\n{en}" : en;
            }
        }
        if (label.Length == 0) label = $"#{m.EntityId & 0xFFFF}";

        // Modèle : uniquement si le stem a changé (mis en file pour
        // construction étalée P0.4 — évite le pic de 263 Duplicate+anim
        // dans la même frame au burst d'entrée).
        if (modelStem.Length > 0 && modelStem != e.ModelStem)
        {
            e.ModelStem = modelStem;
            _pendingVisuals.Enqueue(
                (m.Handle, modelStem, skinTex, npcScale, Now));
            WarmMobModel(modelStem, skinTex);
        }

        // FieldZ = DIRECTION (radians), pas une hauteur : le passer
        // comme z faisait « flotter » les PNJ/mobs partout où le
        // terrain est sous 4 m d'écart (z = facing ∈ [0 ; 6,3]).
        var (gx, gy, gz) = GroundPos(m.X, m.Y, 0f, e.Position.Y);
        e.Position = new Vector3(gx, gy, gz);
        // Direction : natif slot3 = dir (NPCs). Pour les mobs C# le
        // facing arrive en slot4 — on prend le premier non nul.
        var yaw = EdenFacingToYaw(m.FieldZ);
        e.Rotation = new Vector3(0f, yaw, 0f);
        e.Interp.Buffer.Push(Now, gx, gy, gz, yaw);
        e.MoveSpeed = m.FieldSpeed; // slot natif +0x18 = GetMovement
        if ((m.EntityId & 0xFFFF) is >= 54901 and <= 54905)
            GD.Print($"[registry] statue pos ancrée godot={e.Position}");

        if (e.DisplayName != label)
        {
            e.DisplayName = label;
            e.SetLabel(label);
        }
        Spawned?.Invoke(m.Handle, m.EntityId, m.Kind);
        GD.Print($"[registry] +{(m.Kind == 8 ? "npc" : "mob")} " +
            $"#{m.EntityId & 0xFFFF} h=0x{m.Handle:x8} " +
            $"({m.X:F1},{m.Y:F1}) hp={m.Hp}/{m.MaxHp} " +
            $"kind={m.Kind} stem=«{modelStem}» name=«{m.DisplayName}»" +
            // « glb-miss » = modèle irrésolvable (pas juste « pas encore
            // construit » — le visuel est différé, Animator est null à
            // cet instant même quand tout va bien).
            (modelStem.Length > 0 &&
             !AssetCatalog.TryGetGlb($"monster/model/{modelStem}", out _)
                ? " [glb-miss]" : ""));
    }

    /// <summary>265 SeeChar — spawn d'un joueur distant
    /// (handle = charId). Le 265 ne porte PAS le blob d'apparence
    /// (96 o fixes — seulement modelRev) : on demande la fiche 263
    /// via WhoIs (NCID 2) une fois par révision modèle, puis le corps
    /// GLB est construit par <see cref="CharVisual.BuildFromAppearance"/>
    /// à la réception (<see cref="HandleDisplayChar"/>).</summary>
    public void HandleSeeChar(ZoneSeeChar c)
    {
        var e = GetOrCreate(c.CharId, 0, 0);
        e.Hp = c.Hp;
        e.MaxHp = c.MaxHp;
        var (gx, gy, gz) = GroundPos(c.X, c.Y, 0f, e.Position.Y);
        e.Position = new Vector3(gx, gy, gz);
        var yaw = EdenFacingToYaw(c.Facing);
        e.Rotation = new Vector3(0f, yaw, 0f);
        e.Interp.Buffer.Push(Now, gx, gy, gz, yaw);
        e.MoveSpeed = c.MoveSpeed;
        var label = $"Lv{c.Level} char#{c.CharId}";
        e.DisplayName = label;
        e.LabelProminent = true;
        e.SetLabel(label);
        // Apparence : requête WhoIs différée jusqu'au bind session —
        // une par (charId, modelRev) (Revisions[0] = modelRev natif).
        if (c.CharId != _selfCharId && c.Revisions.Length > 0 &&
            (!_whoisSent.TryGetValue(c.CharId, out var rev) ||
             rev != c.Revisions[0]))
        {
            _whoisSent[c.CharId] = c.Revisions[0];
            _whoisWanted.Add(c.CharId);
        }
        Spawned?.Invoke(c.CharId, 0, 0);
        GD.Print($"[registry] +player #{c.CharId} ({c.X:F1},{c.Y:F1})");
    }

    /// <summary>EDEN_FACING_DIAG=1 : logue l'écart cap/déplacement.</summary>
    private static readonly bool FacingDiag =
        System.Environment.GetEnvironmentVariable("EDEN_FACING_DIAG") == "1";

    /// <summary>EDEN_HIT_DIST=1 : logue la distance <b>rendue</b>
    /// attaquant↔joueur au moment d'un 272 reçu sur soi — mesure le
    /// retard de l'interpolation du mob (serveur tape à ~6 u ; un
    /// rendu à 15-25 u = lag de prédiction).</summary>
    private static readonly bool HitDistDiag =
        System.Environment.GetEnvironmentVariable("EDEN_HIT_DIST") == "1";

    /// <summary>EDEN_MOBMOVE_DIAG=1 : logue <c>[mobmove]</c> — par 326 de
    /// mob : dt entre arrivées, latence file principale, pas cur→cur,
    /// écart cur serveur / position rendue (snap), distance dst ; par
    /// frame : saut de position rendue &gt; 1,2 u (téléport) et mob en
    /// « moving » immobile &gt; 120 ms (gel). À lancer sur
    /// <c>//goto 155 56</c> (S717) avec des mobs Lv53.</summary>
    private static readonly bool MobMoveDiag =
        System.Environment.GetEnvironmentVariable("EDEN_MOBMOVE_DIAG") == "1";

    private readonly Dictionary<uint, (double At, Vector2 Cur)> _diagPkt = new();
    private readonly Dictionary<uint, (Vector3 Pos, double StillSince)> _diagFrame = new();

    private void DiagMobMovePacket(RemoteEntity e, ZoneMovePrediction m)
    {
        var h = (uint)m.Handle;
        var at = m.ArrivalSec > 0 ? m.ArrivalSec : Now;
        var (gx, _, gz) = GroundPos(m.CurPos.X, m.CurPos.Y, 0f, e.Position.Y);
        var cur = new Vector2(gx, gz);
        var dt = -1.0; var step = -1f;
        if (_diagPkt.TryGetValue(h, out var prev))
        { dt = at - prev.At; step = cur.DistanceTo(prev.Cur); }
        _diagPkt[h] = (at, cur);
        var rendered = new Vector2(e.Position.X, e.Position.Z);
        var dstDist = m.DstPos is { } d
            ? new Vector2(d.X, d.Y).DistanceTo(
                new Vector2(m.CurPos.X, m.CurPos.Y)) : 0f;
        GD.Print($"[mobmove] pkt h=0x{h:x8} dt={dt * 1000:F0}ms " +
            $"lat={(Now - at) * 1000:F0}ms step={step:F2} " +
            $"snap={cur.DistanceTo(rendered):F2} dst={dstDist:F2} " +
            $"spdObs={e.ObsSpeed:F1} spdAnn={e.MoveSpeed:F1} " +
            $"buf={e.Interp.Buffer.Count}");
    }

    private void DiagMobMoveFrame(RemoteEntity e, uint h)
    {
        var p = e.Position;
        if (_diagFrame.TryGetValue(h, out var f))
        {
            var jump = new Vector2(p.X - f.Pos.X, p.Z - f.Pos.Z).Length();
            if (jump > 1.2f)
                GD.Print($"[mobmove] JUMP h=0x{h:x8} {jump:F2}u en 1 frame");
            var moved = jump > 0.004f;
            var still = moved ? 0.0 : (f.StillSince > 0 ? f.StillSince : Now);
            if (!moved && e.Animator is { InputMoving: true } &&
                Now - still > 0.12 && Now - still < 0.135)
                GD.Print($"[mobmove] FREEZE h=0x{h:x8} immobile " +
                    $"{(Now - still) * 1000:F0}ms alors que InputMoving");
            _diagFrame[h] = (p, still);
        }
        else _diagFrame[h] = (p, 0);
    }

    /// <summary>326 MovePrediction — mouvement interpolé d'une entité.
    /// Positions ×16. Sémantique native : le client marche LUI-MÊME
    /// de cur vers dst à la vitesse de l'entité (MobWander n'émet
    /// qu'une trame par trajet) — dst n'est donc pas une position à
    /// appliquer mais l'extrémité d'une prédiction horodatée :
    /// cur@now + dst@now+dist/vitesse dans le buffer. Pousser dst à
    /// now rendait le mob instantanément à destination = « téléport
    /// malgré l'anim de course » constaté en live. Le « moving » de
    /// l'animator vient du payload (dst présent et ≠ cur — le serveur
    /// n'émet dst que si l'entité bouge, BuildMovePrediction) ; sans
    /// repush sous ~0,45 s il est coupé par la garde de
    /// <see cref="_Process"/> — le serveur n'envoie pas de trame
    /// d'arrêt franc, l'immobilité mesurée prend alors le relais
    /// (chemin d'arrêt de <see cref="EntityAnimator"/>).</summary>
    public void HandleMovePrediction(ZoneMovePrediction m)
    {
        if (!_entities.TryGetValue((uint)m.Handle, out var e) || e.Dying)
            return;
        if (MobMoveDiag && e.Kind != 0) DiagMobMovePacket(e, m);
        // Horodatage = arrivée réseau (pas le passage dans la file
        // principale) ; garde-fou si le drain a pris > 1 s de retard.
        var now = m.ArrivalSec > 0 && Now - m.ArrivalSec < 1.0
            ? m.ArrivalSec : Now;
        var yaw = EdenFacingToYaw(m.FacingFloat);
        var (gx, gy, gz) = GroundPos(m.CurPos.X, m.CurPos.Y, 0f,
            e.Position.Y);
        var dst = m.DstPos;
        var moving = dst.HasValue &&
            (dst.Value.X != m.CurPos.X || dst.Value.Y != m.CurPos.Y);
        if (moving && dst is { } d)
        {
            var (dx, dy, dz) = GroundPos(d.X, d.Y, 0f, e.Position.Y);
            var dist = Mathf.Sqrt((dx - gx) * (dx - gx) +
                (dz - gz) * (dz - gz));
            var speed = PredictSpeed(e);
            if (FacingDiag)
            {
                // Le cap reçu doit regarder vers la destination : le
                // vecteur avant du modèle (−Z tourné de yaw) et le
                // déplacement doivent coïncider (écart ~0°).
                var fw = new Vector2(-Mathf.Sin(yaw), -Mathf.Cos(yaw));
                var mv = new Vector2(dx - gx, dz - gz).Normalized();
                GD.Print($"[facing] h=0x{m.Handle:x8} flag={m.Flag} " +
                    $"ecart={Mathf.RadToDeg(fw.AngleTo(mv)):F1}°");
            }
            e.Interp.Buffer.PushMovement(now, gx, gy, gz, yaw,
                now + dist / speed, dx, dy, dz);
        }
        else
        {
            e.Interp.Buffer.Push(now, gx, gy, gz, yaw);
        }
        e.LastMoveAt = now;
        ObserveSpeed(e, moving, gx, gz, now);
        if (e.Animator != null)
        {
            e.Animator.InputMoving = moving;
            // Anim directionnelle : le Flag du 326 est le record de
            // direction natif de l'émetteur (3 gauche, 5 droite, 1
            // arrière…) ; le cap reçu reste celui du corps.
            if (moving)
            {
                var anim = DirectionalLocomotion.AnimForFlag(m.Flag);
                if (e.Kind != 0) anim = MobMoveAnim(e, anim, now);
                e.Animator.MoveAnim = anim;
                if (e.Animator.RideMount is { } ma &&
                    GodotObject.IsInstanceValid(ma))
                    // Flancs croisés côté monture (contenu .kf inversé).
                    ma.MoveAnim = DirectionalLocomotion.MountAnimFor(
                        DirectionalLocomotion.RecordFromFlag(m.Flag));
            }
        }
    }

    /// <summary>Marche (move01) ou course (move02) d'un mob. Natif
    /// (client_game @0x00781b20, choix de l'action de déplacement) : si
    /// l'état « combat » de l'entité (+0x84) est posé ET que le .kfm a
    /// l'action 102 (<c>FUN_00735690(0x66)</c>) → 102 (move02), sinon 101
    /// (move01). Le serveur ne relaie pas cet état dans le 326 (Flag
    /// identique) : on le déduit de ce que voit le client — ruée 267
    /// (flag 0x6a) ou vitesse observée nettement au-dessus de la vitesse
    /// d'errance annoncée (RoamSpeed au 298, BattleSpeed en
    /// poursuite). Repli move01 si le modèle n'a pas de move02.</summary>
    private static string MobMoveAnim(RemoteEntity e, string anim,
        double now)
    {
        if (anim != "move01" || e.Animator is not { } a) return anim;
        var roam = e.MoveSpeed > 0.5f ? e.MoveSpeed : 2f;
        var chasing = e.ObsSpeed > 0f
            ? e.ObsSpeed > roam * 1.4f + 0.3f
            : now - e.ChaseAt < 1.0;
        var pick = chasing && a.Has("move02") ? "move02" : anim;
        if (MobMoveDiag && pick != e.LastMobMoveAnim)
            GD.Print($"[mobanim] h=0x{e.Handle:x8} {pick} " +
                $"obs={e.ObsSpeed:F1} roam={roam:F1}");
        e.LastMobMoveAnim = pick;
        return pick;
    }

    /// <summary>Vitesse utilisée pour l'horizon de prédiction des
    /// 326/267 : la vitesse annoncée (298 FieldSpeed / 265) sauf quand
    /// l'observée (<see cref="RemoteEntity.ObsSpeed"/>) est supérieure —
    /// les mobs sont annoncés à RoamSpeed (~2 u/s) alors que le serveur
    /// les poursuit à ~7 u/s : sans l'observée, le rendu traînait
    /// derrière la position serveur et leurs coups « arrivaient de
    /// loin ».</summary>
    private static float PredictSpeed(RemoteEntity e)
    {
        var announced = e.MoveSpeed > 0.5f ? e.MoveSpeed : 6f;
        return e.ObsSpeed > announced ? e.ObsSpeed : announced;
    }

    /// <summary>Met à jour <see cref="RemoteEntity.ObsSpeed"/> depuis ce
    /// 326 et règle la vitesse de lecture des anims de marche du joueur
    /// distant (cavalier + monture) comme pour le joueur local : bonus =
    /// (vitesse / vitesse à pied de base − 1) × 100.</summary>
    private void ObserveSpeed(RemoteEntity e, bool moving, float gx, float gz,
        double now)
    {
        if (!moving) { e.ObsPrevAt = 0; return; }
        if (e.ObsPrevAt > 0)
        {
            var dt = now - e.ObsPrevAt;
            var d = new Vector2(gx, gz).DistanceTo(e.ObsPrev);
            if (dt is > 0.02 and < 0.8 && d > 0.02f)
                e.ObsSpeed = MoveAnimSpeed.Smooth(e.ObsSpeed, (float)(d / dt));
            else if (dt >= 0.8)
                // Chaîne rompue (fin de poursuite → errance à RoamSpeed) :
                // une ObsSpeed périmée (7 u/s) faisait marcher le wander
                // trop vite puis figer en bout de segment.
                e.ObsSpeed = 0f;
        }
        e.ObsPrev = new Vector2(gx, gz);
        e.ObsPrevAt = now;
        if (e.ObsSpeed <= 0f || e.Animator == null) return;
        var ma = e.Animator.RideMount is { } m &&
                 GodotObject.IsInstanceValid(m) ? m : null;
        var scale = MoveAnimSpeed.Scale(MoveAnimSpeed.BonusPct(
            MoveAnimSpeed.MulFromObserved(e.ObsSpeed)), ma != null);
        e.Animator.MoveSpeedScale = scale;
        if (ma != null) ma.MoveSpeedScale = scale;
    }

    /// <summary>267 ClientGoto — ordre « aller à » d'une entité
    /// (réponse à TargetUpdate natif : [i32][f32 x][f32 y][u8]).
    /// À l'aggro le serveur y met la position du JOUEUR (ruée du mob,
    /// flag 0x6a) : poussée comme un état instantané elle faisait
    /// « charger » le mob sur le joueur puis revenir avec le 326
    /// suivant — cible proche → prédiction de marche depuis la
    /// position prédite ; cible lointaine (>20 u) ou entité jamais
    /// vue en mouvement → téléport franc (respawn, reposition).</summary>
    public void HandleGoto(ZoneClientGoto m)
    {
        if (!_entities.TryGetValue((uint)m.Handle, out var e) || e.Dying)
            return;
        var (gx, gy, gz) = GroundPos(m.X, m.Y, 0f, e.Position.Y);
        if (e.Kind != 0 && m.Flag == 0x6a) e.ChaseAt = Now;
        if (e.Interp.Buffer.SampleAt(Now, out var px, out var py,
                out var pz, out _))
        {
            var ddx = gx - px; var ddz = gz - pz;
            var dist = Mathf.Sqrt(ddx * ddx + ddz * ddz);
            if (dist * dist <= e.Interp.Buffer.MaxCurCorrectionSq)
            {
                e.Interp.Buffer.PushMovement(Now, px, py, pz,
                    e.Rotation.Y, Now + dist / PredictSpeed(e), gx, gy, gz);
                return;
            }
        }
        e.Interp.PushState(new Vector3(gx, gy, gz),
            e.Rotation.Y, Now);
    }

    /// <summary>264 ClientMoveChar [f32 x][f32 y] — écho de position
    /// self (pas de handle ; broadcast du serveur C#).</summary>
    public void HandleMoveChar(ZoneMoveChar m) =>
        SelfMoved?.Invoke(m.X, m.Y);

    /// <summary>269 LifeEradicate — despawn.</summary>
    public void HandleLifeEradicate(ZoneLifeEradicate m) =>
        Remove((uint)m.Handle);

    /// <summary>294 LeaveNode — despawn (NON PROUVE : même traitement
    /// que 269 si un handle est présent).</summary>
    public void HandleLeaveNode(ZoneLeaveNode m) =>
        Remove((uint)m.Value);

    /// <summary>330 TreasureDisappear — retrait d'un trésor.</summary>
    public void HandleTreasureDisappear(ZoneTreasureDisappear m) =>
        Remove(m.Value);

    /// <summary>329 SeeTreasure — trésor au sol (itemId pour label).</summary>
    public void HandleSeeTreasure(ZoneSeeTreasure t)
    {
        using var _pt = EdenClient.Perf.PerfTrace.Scope($"seeTreasure#{t.ItemId}");
        var e = GetOrCreate(t.Handle, t.ItemId, 0);
        var (gx, gy, gz) = GroundPos(t.X, t.Y, 0f, e.Position.Y);
        e.Position = new Vector3(gx, gy, gz);
        // Plan 03 : libellé = nom d'item résolu (FR/EN via t_item) ;
        // repli « treasure#id » repris par GameScene si la table
        // n'est pas encore chargée. Couleur = qualité (SetTreasureVisual).
        var tname = EdenClient.Data.TextResolver.ItemName(t.ItemId);
        e.DisplayName = tname != null
            ? EdenClient.Data.TreasureRules.Label(tname, t.Count)
            : $"treasure#{t.ItemId}×{t.Count}";
        // A6.3 : modèle coffre par c_item TypeCode (repli G00001 si
        // vide/invalide — plan 03) + halo/label par qualité.
        var def = Hud.InventoryWindow.LookupDef(t.ItemId);
        var family = EdenClient.Data.TreasureRules.Family(def?.TypeCode);
        Node3D? glb = null;
        if (AssetCatalog.TryGetGlb($"item/model/{family}", out var gp))
            glb = AssetCatalog.LoadGlbNode(gp);
        e.SetTreasureVisual(glb, Hud.ItemTooltip.QualityColor(
            def?.Quality ?? 0), family);
        e.SetLabel(e.DisplayName);
        Spawned?.Invoke(t.Handle, t.ItemId, 0);
    }

    /// <summary>Levé quand la patience (attr 0x13) d'une entité change —
    /// le cadre de cible rafraîchit sa barre BP.</summary>
    public event Action<uint>? PatienceChanged;

    /// <summary>280 UpdateAttr — attributs (attrId 0 = HP).</summary>
    public void HandleUpdateAttr(ZoneUpdateAttr m)
    {
        if (!_entities.TryGetValue((uint)m.Handle, out var e)) return;
        // H6 : attr 0 = HP courant (p2 recopie le courant, ce n'est
        // PAS le max) ; attr 0x11 = HP max.
        if (m.AttrId == 0)
        {
            e.Hp = m.Current;
            if (e.MaxHp < e.Hp) e.MaxHp = e.Hp;
            e.UpdateHpBar();
        }
        else if (m.AttrId == 0x11)
        {
            e.MaxHp = m.Current;
            e.UpdateHpBar();
        }
        else if (m.AttrId == 0x13)
        {
            // Patience (jauge Break Point) — i32 courant, éventuellement
            // négatif ; < 1 sur un mob à faiblesses = brisé. La valeur
            // 0 est aussi l'état par défaut des mobs SANS jauge BP :
            // n'exiger la coquille retirée qu'après une patience
            // positive reçue (sinon tous les mobs à faiblesses
            // apparaissent « cassés » à pleins PV).
            e.Patience = m.Current;
            if (e.Patience > 0) e.PatienceSeen = true;
            e.SetBroken(e.WeakMask != 0 && e.PatienceSeen &&
                e.Patience < 1);
            PatienceChanged?.Invoke((uint)m.Handle);
        }
    }

    /// <summary>380 MobTransform — le mob change de template :
    /// on ré-évalue le modèle si possible, sinon on le marque.</summary>
    public void HandleMobTransform(ZoneMobTransform m)
    {
        if (!_entities.TryGetValue((uint)m.Handle, out var e)) return;
        e.EntityId = m.NewTemplateId & 0xFFFF;
        var info = ResolveMonster?.Invoke(e.EntityId);
        if (info is { } mi)
        {
            e.ModelStem = mi.Model;
            var glb = LoadMobGlb(mi.Model);
            e.SetVisual(glb, isPlayer: false, animStem: mi.Model);
            e.SetLabel(EdenClient.Data.BreakPointUi.FormatMobLabel(
                mi.Level, mi.Name));
        }
    }

    /// <summary>Vide le registre (sortie de node / déconnexion).</summary>
    public void Clear()
    {
        _groundCache.Clear(); _groundQueue.Clear(); _groundPending.Clear();
        _pendingVisuals.Clear();
        foreach (var e in _entities.Values) e.QueueFree();
        _entities.Clear();
        foreach (var (e, _) in _corpses)
            if (GodotObject.IsInstanceValid(e)) e.QueueFree();
        _corpses.Clear();
        _whoisWanted.Clear();
        _whoisSent.Clear();
    }

    // ------------------------------------------------------------------
    // Combat / mort / apparence — trames 272/305/263 routées ici par le
    // binding session paresseux de <see cref="_Process"/> (la session
    // vit dans SessionCarry pour le flux écrans ; en mode --live
    /// direct elle est privée à GameScene — limite documentée, même
    /// que l'envoi AutoAttack).
    // ------------------------------------------------------------------

    /// <summary>272 ClientMeleeAttack — résolution pure via
    /// <see cref="CombatFx"/> + <see cref="MotionTable.Shared"/> :
    /// anim d'attaque de l'attaquant (kfId→nom exact quand le stem
    /// est connu), hurt01/death01 sur la cible. L'attaquant self
    /// (charId) n'est pas dans le registre → sibling « Player ».</summary>
    public void HandleMeleeAttack(ZoneClientMeleeAttack m)
    {
        var atkStem = ResolveCombatantStem(m.Attacker);
        var atkAnim = CombatFx.AttackAnim(MotionTable.Shared,
            atkStem, m.MotionKfId);
        if (atkAnim != null)
            PlayOn(m.Attacker, atkAnim);

        var hitAnim = CombatFx.HitAnim(m.HitType);
        if (CombatFx.IsFatal(m.HitType))
            MarkDying(m.Target);
        if (hitAnim != null)
            PlayOn(m.Target, hitAnim);
        if (_entities.TryGetValue(m.Attacker, out var atk))
        {
            atk.LastAtkTarget = m.Target;
            atk.LastAtkTime = Now;
        }
        GD.Print($"[combat] hit atk=0x{m.Attacker:x8} tgt=0x{m.Target:x8} " +
            $"dmg={m.Damage} type={m.HitType} kf={m.MotionKfId}" +
            (atkAnim != null ? $" → {atkAnim}" : ""));
        if (HitDistDiag && m.Target == _selfCharId &&
            _entities.TryGetValue(m.Attacker, out var a) &&
            a.IsInsideTree() && SelfPlayer() is { } sp && sp.IsInsideTree())
            GD.Print($"[hit-dist] rendue={a.GlobalPosition
                .DistanceTo(sp.GlobalPosition):F1}u " +
                $"obsSpeed={a.ObsSpeed:F1} annoncée={a.MoveSpeed:F1}");
    }

    /// <summary>Destinataire visuel d'un 268 ClientSay.</summary>
    public enum SayTarget
    {
        /// <summary>Handle inconnu / 0 : chat seul.</summary>
        None,
        /// <summary>Joueur (distant ou self) : bulle + ligne de chat.</summary>
        Player,
        /// <summary>PNJ / monstre : bulle seule (MonsterSay natif).</summary>
        Npc,
    }

    /// <summary>268 ClientSay — pose la bulle 3D sur l'entité émettrice
    /// (<paramref name="handle"/> = charId joueur ou sight-handle
    /// NPC/mob). Spec <c>ref-shots/shot_0082_000338.png</c>. Le joueur
    /// local n'est pas dans le registre : bulle sur
    /// <paramref name="selfPlayer"/>.</summary>
    public SayTarget ShowSay(uint handle, string text,
        PlayerController? selfPlayer)
    {
        if (string.IsNullOrWhiteSpace(text)) return SayTarget.None;
        if (_entities.TryGetValue(handle, out var e))
        {
            e.ShowBubble(text);
            return e.Kind == 0 ? SayTarget.Player : SayTarget.Npc;
        }
        if (handle != 0 && handle == _selfCharId && selfPlayer != null)
        {
            selfPlayer.ShowBubble(text);
            return SayTarget.Player;
        }
        return SayTarget.None;
    }

    /// <summary>495 ClientPlayMonsterAction <c>[u32 handle][s16 kfId]</c>
    /// (CAI::SetEventAction) : joue la motion kfId sur l'entité —
    /// motion.ini exact puis convention de nommage
    /// (<see cref="Scene.MonsterActionAnim"/>). Retourne le nom d'anim
    /// joué ("" si aucune dispo).</summary>
    public string HandleMonsterAction(ZoneClientPlayMonsterAction m)
    {
        var handle = (uint)m.Field1;
        var stem = ResolveCombatantStem(handle);
        var names = Scene.MonsterActionAnim.Candidates(MotionTable.Shared,
            stem, m.Field2);
        if (names.Count == 0) return "";
        string played = "";
        if (_entities.TryGetValue(handle, out var e))
        {
            if (e.Animator != null)
                played = e.Animator.PlayOnceAny(names.ToArray());
        }
        else if (handle == _selfCharId &&
                 SelfPlayer()?.Animator is { } selfAnim)
            played = selfAnim.PlayOnceAny(names.ToArray());
        GD.Print($"[scene] action 495 handle=0x{handle:x8} kf={m.Field2} " +
                 $"→ {(played.Length > 0 ? played : "(aucune anim)")}");
        return played;
    }

    /// <summary>305 ClientDie — la victime joue son anim de mort et
    /// est marquée mourante (le despawn 269 qui suit ~5 s après est
    /// alors gracié si l'anim tourne encore).</summary>
    public void HandleClientDie(ZoneClientDie m)
    {
        if (MarkDying(m.Victim))
            GD.Print($"[combat] die victim=0x{m.Victim:x8} " +
                     $"killer=0x{m.Killer:x8}");
    }

    /// <summary>263 ClientDisplayChar — réponse WhoIs : apparence
    /// 32 o → corps GLB + nom réel du joueur distant.</summary>
    public void HandleDisplayChar(ZoneClientDisplayChar m)
    {
        if (!_entities.TryGetValue(m.CharId, out var e) || e.Kind != 0)
            return;
        var body = CharVisual.BuildFromAppearance(m.Appearance);
        if (body != null)
        {
            e.SetVisual(body, isPlayer: true);
            e.ModelStem = CharModelSpec.BodyStem(m.Appearance);
        }
        e.ClassId = m.ClassId;
        var label = $"Lv{m.Level} {m.CharName}";
        e.DisplayName = label;
        e.LabelProminent = true;
        e.SetLabel(label);
        // Queue variable du 263 : mask u8 + champs conditionnels
        // (rideKey en bit6) puis les sets d'équipement — le set0
        // (conteneur « tenu », slots 0..10) alimente l'habillage du
        // corps distant, même pipeline que le joueur local
        // (ApplyWornEquipment → CharVisual.ApplyEquipment).
        var (rideKey, worn) = ParseDisplayTail(m.Rest);
        e.SetRide(rideKey);
        e.WornItems.Clear();
        e.CostumeItems.Clear();   // re-poussés par les 276 suivant le 263
        foreach (var (loc, itemId) in worn)
            e.WornItems[loc] = itemId;
        e.ApplyWornEquipment();
        GD.Print($"[registry] player #{m.CharId} → «{m.CharName}» " +
                 $"(corps={e.ModelStem}{(body != null ? " ok" : " KO")}" +
                 $"{(rideKey > 0 ? $" ride={rideKey}" : "")}" +
                 $" équip={worn.Count})");
    }

    /// <summary>Décode la queue variable du 263 (tout ce qui suit le
    /// blob apparence 0x20) — layout
    /// <c>ClientDisplayCharCodec.Build</c> / Serialize natif
    /// @0x081e03f0 :
    /// <code>
    /// [u8 mask] champs conditionnels : bit0 i64 hp, bit1 i64 maxHp,
    ///   bit2 i32 maxHp32, bit3 i32 maxMp, bit4 u8 +0x41 (jamais émis
    ///   par le serveur C# mais décodé), bit5 u8 échelle,
    ///   bit6 u32 rideKey, bit7 u32 transformTpl
    /// [u32 mask équipement set0] bit i = slot i (0..10) occupé, puis
    ///   par slot croissant : [u16 itemId] + corps EquipView
    ///   ([u8 flags][str][u24 mask][cond][34B tail] —
    ///   <see cref="NativeItemRecord.TryReadEquipView"/>)
    /// [u32 mask set1][records légers 22B] — 2e conteneur, ignoré.
    /// </code>
    /// Retourne (rideKey, loc→itemId) ; <c>worn</c> est partiel si la
    /// queue est tronquée en cours de record, vide si absente.</summary>
    private static (int RideKey, Dictionary<int, int> Worn)
        ParseDisplayTail(byte[] rest)
    {
        var worn = new Dictionary<int, int>();
        if (rest.Length < 1) return (0, worn);
        var r = new BinReader(rest);
        var mask = r.ReadByte();
        var skip = (int bytes) =>
        {
            if (r.Remaining < bytes) return false;
            r.ReadBytes(bytes);
            return true;
        };
        if ((mask & 0x01) != 0 && !skip(8)) return (0, worn);  // hp i64
        if ((mask & 0x02) != 0 && !skip(8)) return (0, worn);  // maxHp i64
        if ((mask & 0x04) != 0 && !skip(4)) return (0, worn);  // maxHp32
        if ((mask & 0x08) != 0 && !skip(4)) return (0, worn);  // maxMp
        if ((mask & 0x10) != 0 && !skip(1)) return (0, worn);  // +0x41 u8
        if ((mask & 0x20) != 0 && !skip(1)) return (0, worn);  // échelle
        var rideKey = 0;
        if ((mask & 0x40) != 0)
        {
            if (r.Remaining < 4) return (0, worn);
            rideKey = r.ReadInt();                             // rideKey u32
        }
        // bit7 — transformTpl u32.
        if ((mask & 0x80) != 0 && !skip(4)) return (rideKey, worn);
        // Équipement set0 : mask u32 des slots 0..10 puis records
        // [u16 itemId][corps EquipView] dans l'ordre croissant.
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

    /// <summary>276 ClientChangeEquip — delta d'un slot d'équipement
    /// broadcasté aux observateurs (<c>NotifyChangeEquipment</c> natif,
    /// soi exclu — <c>PlayerEquipment</c> couvre déjà le self via
    /// GameScene) : met à jour <see cref="RemoteEntity.WornItems"/>
    /// d'un perso distant puis ré-applique l'habillage du corps.
    /// Le conteneur 0 (« tenu ») pilote le visuel ; les conteneurs 2-4
    /// (pages costume) le recouvrent (A6.6).</summary>
    public void HandleChangeEquip(ZoneChangeEquip m)
    {
        if (m.Container is not (0 or (>= 2 and <= 4)) ||
            (uint)m.CharId == _selfCharId ||
            !_entities.TryGetValue((uint)m.CharId, out var e) ||
            e.Kind != 0)
            return;
        if (m.Container != 0)
        {
            // A6.6 : page costume → fusionnée au rendu sur la tenue.
            if (m.ItemId == 0) e.CostumeItems.Remove((m.Container, m.Slot));
            else e.CostumeItems[(m.Container, m.Slot)] = m.ItemId;
            e.ApplyWornEquipment();
            return;
        }
        if (m.ItemId == 0) e.WornItems.Remove(m.Slot);
        else e.WornItems[m.Slot] = m.ItemId;
        e.ApplyWornEquipment();
    }

    /// <summary>Marque une entité (ou le joueur self) mourante —
    /// joue death01 via <see cref="RemoteEntity.Die"/>. Retourne true
    /// si une victime a été trouvée.</summary>
    // Libération étalée : un cadavre porte ~800 objets (meshes, pistes d'animation) ; les libérer
    // d'un coup coûtait ~65 ms (« mini freeze » après chaque kill). On masque l'entité puis on
    // détruit ses descendants par paquets, feuilles d'abord.
    private readonly Queue<Node> _slowFree = new();

    /// <summary>Compteurs pour le banc --kill-loop : arriérés de
    /// libération des cadavres et cadavres en attente de grâce.</summary>
    internal int DebugSlowFreeQueued => _slowFree.Count;
    internal int DebugCorpseCount => _corpses.Count;

    private void SlowFree(Node e)
    {
        if (!GodotObject.IsInstanceValid(e)) return;
        if (e is Node3D n3) n3.Visible = false;
        if (e.GetParent() != null) e.GetParent().RemoveChild(e);
        var all = new List<Node>();
        void Walk(Node n) { foreach (var c in n.GetChildren()) Walk(c); all.Add(n); }   // post-ordre : feuilles d'abord
        Walk(e);
        foreach (var n in all) _slowFree.Enqueue(n);
    }

    private void PumpSlowFree()
    {
        for (var i = 0; i < 10 && _slowFree.Count > 0; i++)
        {
            var n = _slowFree.Dequeue();
            if (GodotObject.IsInstanceValid(n)) n.Free();
        }
    }

    private bool MarkDying(uint handle)
    {
        if (_entities.TryGetValue(handle, out var e))
        {
            e.Die();
            return true;
        }
        if (handle == _selfCharId &&
            SelfPlayer()?.Animator is { } selfAnim)
        {
            selfAnim.AutoLocomotion = false;
            selfAnim.PlayOnceAny("death01", "die01");
            return true;
        }
        return false;
    }

    /// <summary>Joue l'anim one-shot sur l'entité du registre ou sur
    /// le joueur self (handle = charId).</summary>
    private void PlayOn(uint handle, string anim)
    {
        if (_entities.TryGetValue(handle, out var e))
            e.PlayOnce(anim);
        else if (handle == _selfCharId)
            SelfPlayer()?.Animator?.PlayOnce(anim);
    }

    /// <summary>Joueur local (nœud <c>Player</c> de la scène, sibling du
    /// registre) — résolution lazy : le champ n'était jamais assigné, ce
    /// qui rendait inertes les branches anim self (hurt/death).</summary>
    private PlayerController? SelfPlayer()
    {
        if (_player != null && !GodotObject.IsInstanceValid(_player))
            _player = null;
        _player ??= GetTree()?.Root?
            .FindChild("Player", true, false) as PlayerController;
        return _player;
    }

    /// <summary>Stem KFM d'un combattant : <see cref="RemoteEntity.ModelStem"/>
    /// pour les entités (stem monstre ou lettre corps remote), lettre
    /// du corps self pour le joueur local.</summary>
    private string? ResolveCombatantStem(uint handle)
    {
        if (_entities.TryGetValue(handle, out var e) &&
            e.ModelStem.Length > 0)
            return e.ModelStem;
        if (handle == _selfCharId)
            return CharModelSpec.BodyStem(SessionCarry.Character?.Appearance);
        return null;
    }

    /// <summary>Binding paresseux : la session apparaît quand le flux
    /// écrans l'a posée dans <see cref="SessionCarry"/> — les handlers
    /// s'enregistrent une seule fois (272/305/263/276). Sur le thread
    /// réseau les handlers ne font que copier les octets bruts ; la
    /// désérialisation et l'application scène se font ici (thread
    /// principal) via la file <see cref="_netPending"/>.</summary>
    public override void _Process(double delta)
    {
        using var _procScope = EdenClient.Perf.PerfTrace.Scope("proc.Registry");
        if (_session == null && SessionCarry.Session is { } s)
            BindSession(s);
        {
            using var _pt = EdenClient.Perf.PerfTrace.Scope("reg.net");
            // Budget par frame : un burst (entrée de node, kill +
            // loot + cascade level-up) se répartit sur plusieurs
            // images au lieu d'un pic de ~40 ms. L'ordre est préservé
            // (file FIFO) ; le reliquat part en tête la frame suivante.
            var netT0 = Time.GetTicksUsec();
            const ulong netBudgetUs = 6000;
            while (_netPending.TryDequeue(out var act))
            {
                var actT0 = Time.GetTicksUsec();
                try { act(); }
                catch (Exception ex)
                {
                    GD.PrintErr($"[registry] dispatch : {ex}");
                }
                var actMs = (Time.GetTicksUsec() - actT0) / 1000.0;
                if (actMs >= 4.0)
                    // Une action seule peut dépasser le budget (spawn
                    // de visuel, decode KF) — on la nomme pour
                    // l'attribuer (closure → méthode englobante).
                    EdenClient.Perf.PerfTrace.Event(
                        $"regact:{act.Method.DeclaringType?.Name}" +
                        $" {actMs:F0}ms");
                // Cap dur : le producteur réseau remplit la file
                // pendant une rafale de combat (272/495/280/326…) —
                // exiger Count>0 pour couper laissait le drain suivre
                // toute la rafale en direct (reg.net ~39 ms, gel ~0,1 s).
                if (Time.GetTicksUsec() - netT0 >= netBudgetUs) break;
            }
        }
        {
            using var _pt = EdenClient.Perf.PerfTrace.Scope("reg.visual");
            DrainPendingVisuals(4.0);
        }
        {
            using var _pt = EdenClient.Perf.PerfTrace.Scope("reg.tick");
            // Tick mutualisé des interpolateurs + animators d'entités —
            // évite ~500 callbacks natifs→C# par frame (S001 : ~260
            // entités × 2 composants ; mesuré au --perf).
            foreach (var e in _entities.Values)
            {
                e.Interp?.Tick(delta);
                if (MobMoveDiag && e.Kind != 0 && e.Animator != null &&
                    Now - e.LastMoveAt < 2.0)
                    DiagMobMoveFrame(e, e.Handle);
                if (e.Animator != null)
                {
                    // Pas de trame d'arrêt serveur : passé 0,45 s sans 326
                    // l'input « moving » est périmé — l'animator mesure
                    // alors l'immobilité réelle (arrêt franc) ou continue
                    // via la fenêtre de décélération si l'entité glisse
                    // encore vers sa destination prédite.
                    if (e.Animator.InputMoving && Now - e.LastMoveAt > 0.45)
                        e.Animator.InputMoving = false;
                    e.Animator.Tick(delta);
                }
            }
        }
        {
            using var _pt = EdenClient.Perf.PerfTrace.Scope("reg.pump");
            PumpSlowFree();
        }
        {
            using var _pt = EdenClient.Perf.PerfTrace.Scope("reg.corpse");
            // Expiration des cadavres graciés.
            for (var i = _corpses.Count - 1; i >= 0; i--)
            {
                if (!GodotObject.IsInstanceValid(_corpses[i].Entity) ||
                    Now >= _corpses[i].FreeAt)
                {
                    SlowFree(_corpses[i].Entity);
                    _corpses.RemoveAt(i);
                }
            }
        }
        FlushWhois();
    }

    /// <summary>Enregistre les handlers NCID combat/apparence sur la
    /// session (272 coups, 305 mort, 263 fiche perso, 276 équipement
    /// distant). Chaque handler copie les octets sur le thread réseau
    /// puis poste parse+application ici (file <see cref="_netPending"/>).</summary>
    private void BindSession(EdenSession s)
    {
        _session = s;
        _selfCharId = (uint)(SessionCarry.Character?.Id ?? 0);
        MotionTable.ResolveFile ??= AssetCatalog.ResolveRawAsset;
        void Bind(NetMessage proto, Action<BinReader> apply) =>
            s.OnZone(proto.Opcode, r =>
            {
                // Thread réseau : copie brute uniquement — le parse
                // et l'application sont faits dans la file principale.
                byte[] copy = r.ReadBytes(r.Remaining);
                _netPending.Enqueue(() =>
                {
                    var reader = new BinReader(copy);
                    apply(reader);
                });
            });
        Bind(new ZoneClientMeleeAttack(), r =>
        {
            var m = new ZoneClientMeleeAttack();
            m.Deserialize(r);
            HandleMeleeAttack(m);
        });
        Bind(new ZoneClientDie(), r =>
        {
            var m = new ZoneClientDie();
            m.Deserialize(r);
            HandleClientDie(m);
        });
        Bind(new ZoneClientDisplayChar(), r =>
        {
            var m = new ZoneClientDisplayChar();
            m.Deserialize(r);
            HandleDisplayChar(m);
        });
        // 276 distant : GameScene enregistre déjà un handler (self →
        // PlayerEquipment) — OnZone accumule les handlers, le nôtre
        // ne traite que les charIds du registre (HandleChangeEquip
        // ignore le self).
        Bind(new ZoneChangeEquip(), r =>
        {
            var m = new ZoneChangeEquip();
            m.Deserialize(r);
            HandleChangeEquip(m);
        });
    }

    /// <summary>Envoie les WhoIs en attente (flag=1 → fiche 263) —
    /// fire-and-forget tracé.</summary>
    private void FlushWhois()
    {
        var s = _session;
        if (s == null || _whoisWanted.Count == 0) return;
        foreach (var charId in _whoisWanted)
        {
            var send = s.SendZoneAsync(new ZoneWhoIs
            {
                CharId = (int)charId, Flag = 1,
            }, CancellationToken.None);
            _ = send.ContinueWith(t =>
                GD.PrintErr($"[registry] WhoIs {charId} : {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
        }
        _whoisWanted.Clear();
    }

    // ------------------------------------------------------------------

    /// <summary>Position Eden (x, y sol, z hauteur) → Godot ; si z est
    /// absent/nul on snappe le terrain via <see cref="GroundHeight"/>.
    /// Dans tous les cas l'entité ne descend JAMAIS sous le terrain :
    /// le z serveur est parfois périmé (spawn 298 sous la map —
    /// symptôme H2), on borne Y ≥ hauteur sol. <paramref name="refY"/>
    /// = Y courant de l'entité (continuité d'étage) ; 0 (spawn) →
    /// Y du joueur local via <see cref="RefGroundY"/>.</summary>
    private (float X, float Y, float Z) GroundPos(float ex, float ey,
        float ez, float refY = 0f)
    {
        var g = WorldConv.ToGodot(ex, ey, ez);
        // M10 : le z serveur dérive du heightfield (PNJ flottants ou
        // enterrés) — écart ≤ 4 m = erreur de donnée, on recale ; plus
        // haut = plateforme / pont / volant, on garde le z serveur.
        var terrainH = GroundHeight?.Invoke(g.X, g.Z);
        if (terrainH is { } h)
        {
            if (ez == 0f || g.Y < h || g.Y - h <= 4f) g.Y = h;
        }
        if (PhysicsGround != null)
        {
            // Sol physique résolu en frame physique, mis en cache par
            // case : donjons sans heightfield (terrain absent) ET
            // vérification des sols OBJ au-dessus du terrain (ponts,
            // dalles — le heightfield seul pose l'entité SOUS la
            // structure, symptôme « entité sous la map »).
            var key = GroundKey(g.X, g.Z);
            if (_groundCache.TryGetValue(key, out var cy)) g.Y = cy;
            else
            {
                // Case pas encore résolue (frame physique à venir), sans
                // heightfield : g.Y valait 0 (ez=0) → la destination
                // d'un 326 était poussée au niveau 0 et le mob, qui
                // interpole Y de cur vers dst, s'ENFONÇAIT sous le sol
                // pendant tout le trajet (invisible en marche, visible à
                // l'arrêt une fois cur résolu). Continuité d'étage :
                // on garde le Y courant de l'entité en attendant.
                if (terrainH == null && ez == 0f && refY != 0f)
                    g.Y = refY;
                if (_groundPending.Add(key))
                // Référence d'étage : le z wire quand il est réel
                // (ez≠0 — la zone le résout depuis la scène), sinon le
                // plancher du joueur local (même salle la plupart du
                // temps) ; g.Y = terrain-snappé en dernier recours.
                _groundQueue.Enqueue((key, g.X, g.Z,
                    refY != 0f ? refY
                        : ez != 0f ? g.Y
                        : (RefGroundY?.Invoke() ?? g.Y),
                    terrainH ?? float.NaN));
            }
        }
        return (g.X, g.Y, g.Z);
    }

    /// <summary>Direction Eden (radians autour de +Z, repère Z-up) →
    /// yaw Godot autour de Y. Godot dir = (cosθ, −sinθ) en XZ ;
    /// yaw modèle = atan2(−dx, −dz) (même formule que PlayerController).
    /// </summary>
    public static float EdenFacingToYaw(float facing)
    {
        // dir natif = 3π/2 − cap (ChangeFaceDir) — voir EdenDir.
        return EdenClient.Game.EdenDir.ToYaw(facing);
    }

    /// <summary>Retourne l'entité ou la crée (visuel placeholder tant
    /// qu'aucun modèle n'est résolu).</summary>
    private RemoteEntity GetOrCreate(uint handle, int entityId, byte kind)
    {
        if (_entities.TryGetValue(handle, out var e))
            return e;
        e = new RemoteEntity
        {
            Name = $"E_{handle:x8}",
            Handle = handle,
            EntityId = entityId & 0xFFFF,
            Kind = kind,
        };
        e.Interp = new NetworkInterpolator { Name = "Interp" };
        e.AddChild(e.Interp);
        // Ombre « blob » au sol (Decal projeté ~0,6 u au-dessus des
        // pieds — même convention que les NPC de scène MapLoader).
        var shadow = BlobShadow.Make(0.7f);
        shadow.Position = new Vector3(0f, 0.6f, 0f);
        e.AddChild(shadow);
        e.SetVisual(null, isPlayer: kind == 0 && entityId == 0);
        AddChild(e);
        // Tick mutualisé par EntityRegistry._Process — un callback
        // managé par frame pour toutes les entités au lieu d'un par
        // composant (~260 × 2 sur S001). Après l'entrée dans
        // l'arbre : posé hors arbre le flag ne tenait pas
        // (proc-dump : les ~256 Interp restaient en process).
        e.Interp.SetProcess(false);
        _entities[handle] = e;
        return e;
    }

    /// <summary>Tente de résoudre le GLB du modèle — mobs/NPC vivent
    /// sous <c>monster/model/&lt;stem&gt;.glb</c> (stems M025, m117…
    /// issus de scene.ini / c_monster / c_npc).</summary>
    private static Node3D? LoadMobGlb(string stem)
    {
        if (stem.Length == 0) return null;
        return AssetCatalog.TryGetGlb($"monster/model/{stem}", out var p)
            ? AssetCatalog.LoadGlbNode(p)
            : null;
    }

    /// <summary>Applique le modèle résolu à une entité existante
    /// (spawn différé — appelé par <see cref="HandleSeeMob"/> via
    /// GameScene après résolution).</summary>
    public void ApplyModel(uint handle, string stem, bool isPlayer)
    {
        if (!_entities.TryGetValue(handle, out var e)) return;
        e.SetVisual(LoadMobGlb(stem), isPlayer, animStem: stem);
    }

    /// <summary>Despawn : retrait immédiat, sauf entité mourante dont
    /// l'anim death tourne encore — libération différée à
    /// <see cref="CorpseGraceSec"/> (le 269 serveur arrive ~5 s après
    /// le 305 ; la grâce ne couvre que la fin d'anim éventuelle).</summary>
    private void Remove(uint handle)
    {
        using var _pt = EdenClient.Perf.PerfTrace.Scope("registry.Remove");
        if (!_entities.Remove(handle, out var e)) return;
        if (e.Dying && e.Animator?.OneShotPlaying == true &&
            GodotObject.IsInstanceValid(e))
        {
            _corpses.Add((e, Now + CorpseGraceSec));
        }
        else
        {
            SlowFree(e);
        }
        _whoisSent.Remove(handle);
        _whoisWanted.Remove(handle);
        Despawned?.Invoke(handle);
        GD.Print($"[registry] -h=0x{handle:x8} " +
            $"(reste {_entities.Count})");
    }
}

/// <summary>
/// Résolution fidèle du modèle d'un NPC : <c>data/db/c_npc.ini</c>
/// (npcId → code « n002030 ») puis <c>data/db/npclist.ini</c>
/// (code → « m002.kfm » + texture variante « m00203.dds » + scale).
/// Le col « motion » de la scène ini n'est qu'un nom d'anim —
/// certains NPC affichaient un modèle générique (M014) ou un
/// placeholder alors que le vrai GLB existe (symptôme H4).
///
/// Chargement paresseux + thread-safe (MapLoader tourne en tâche de
/// fond ; les tables restent en mémoire).
/// </summary>
public static class NpcModelTable
{
    /// <summary>Modèle résolu : stem GLB, texture variante, échelle.</summary>
    public readonly struct Def
    {
        /// <summary>Stem du modèle (« m002 » → monster/model/m002.glb).</summary>
        public string Stem { get; init; }
        /// <summary>Texture variante (« m00203.dds » →
        /// monster/texture/m00203.png) — "" = texture par défaut.</summary>
        public string Texture { get; init; }
        /// <summary>Échelle du modèle (npclist col2, 1.0 par défaut).</summary>
        public float Scale { get; init; }
    }

    private static Dictionary<int, Def>? _defs;
    private static bool _tried;
    private static readonly object _lock = new();

    /// <summary>Modèle d'un npcId, ou null si inconnu/fichiers absents.</summary>
    public static Def? Resolve(int npcId)
    {
        EnsureLoaded();
        return _defs != null && _defs.TryGetValue(npcId, out var d)
            ? d : null;
    }

    private static void EnsureLoaded()
    {
        lock (_lock)
        {
            if (_tried) return;
            _tried = true;
            try
            {
                // npclist.ini : [Table] DB<n>=code,kfm,scale,...tex,...
                // col0 = code « n002030 », col1 = « m002.kfm »,
                // col2 = scale, col8 = texture variante.
                var listPath = AssetCatalog.ResolveRawAsset(
                    "data/db/npclist.ini");
                var npcPath = AssetCatalog.ResolveRawAsset(
                    "data/db/c_npc.ini");
                if (listPath == null || npcPath == null)
                {
                    GD.PrintErr("[npc] npclist.ini/c_npc.ini absents — " +
                        "modèles NPC par scène seulement");
                    return;
                }
                var byCode = new Dictionary<string, Def>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var raw in Vfs.EdenVfs.ReadLines(listPath))
                {
                    var eq = raw.IndexOf('=');
                    if (eq <= 0 || !raw.StartsWith("DB",
                            StringComparison.Ordinal))
                        continue;
                    var f = raw[(eq + 1)..].Split(',');
                    if (f.Length < 2 || f[1].Length < 4) continue;
                    var stem = f[1].Trim();
                    if (stem.EndsWith(".kfm",
                            StringComparison.OrdinalIgnoreCase))
                        stem = stem[..^4];
                    var sc = EdenClient.Data.IniTable.GetFloat(f, 2);
                    byCode[f[0].Trim()] = new Def
                    {
                        Stem = stem,
                        Texture = f.Length > 8 ? f[8].Trim() : "",
                        Scale = sc > 0f ? sc : 1f,
                    };
                }
                var npc = EdenClient.Data.IniTable.ParseFile(npcPath,
                    EdenClient.Data.EdenEncoding.Big5);
                var defs = new Dictionary<int, Def>(npc.Rows.Count);
                foreach (var row in npc.Rows)
                {
                    if (row.Length < 2 ||
                        !int.TryParse(row[0], out var id))
                        continue;
                    if (byCode.TryGetValue(row[1].Trim(), out var d))
                        defs[id] = d;
                }
                _defs = defs;
                GD.Print($"[npc] table : {defs.Count} npcId → " +
                         $"{byCode.Count} codes npclist");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[npc] table : {ex.Message}");
            }
        }
    }
}
