using System.Text.RegularExpressions;
using Godot;

namespace EdenClient.World;

/// <summary>Classe physique d'un mesh « helper » (collision masquée) :
/// <see cref="Wall"/> = bloqueur horizontal sur la layer 1,
/// <see cref="Floor"/> = sol marchable sur la layer dédiée
/// <see cref="WalkableGround.FloorLayer"/> (jamais de blocage
/// horizontal — le joueur la traverse et y marche via raycast).</summary>
public enum HelperClass
{
    /// <summary>Bloqueur (murs, clôtures, anti-caméra) — layer 1.</summary>
    Wall,
    /// <summary>Sol marchable (dalles, ponts, rampes, primitives
    /// génériques) — <see cref="WalkableGround.FloorLayer"/>.</summary>
    Floor,
    /// <summary>Coquille de blocage fermée (groupes NIF
    /// « block »/« nocamerablock » — l'intérieur sert de sol et les
    /// faces de murs) — <see cref="WalkableGround.WallLayer"/> +
    /// <see cref="WalkableGround.FloorLayer"/>.</summary>
    Both,
}

/// <summary>
/// Transforme les matériaux d'un GLB instancié en rendu « anime » :
///
///   - <b>Meshes helper invisibles</b> (murs de collision natifs) :
///     les nœuds dont TOUTES les surfaces n'ont pas de texture
///     (« true wall », « blo », « Box01 », « floor1 », « nocamera »…
///     — vérifié sur S001 : ~180 modèles, 1-3 helpers chacun) sont
///     masqués (<c>Visible=false</c>, meta <c>collision_helper</c>
///     pour réutilisation par un futur lot physique).
///   - <b>Matériaux opaques texturés</b> → ShaderMaterial toon
///     (<c>res://shaders/toon_lit.gdshader</c> : 3 paliers doux +
///     rim léger), texture/albedo recopiés.
///   - <b>Matériaux alpha</b> (feuillage, glaces, blends) →
///     StandardMaterial3D ajusté in-place : metallic=0, specular off,
///     roughness=1, cull disabled — l'alpha test/blend du glTF est
///     conservé tel quel.
/// </summary>
public static class ToonMaterializer
{
    private static readonly Dictionary<Material, Material> _toonCache =
        new();
    private static Shader? _toonShader;

    /// <summary>Vide le cache à la sortie de la scène de jeu
    /// (RefCounted — le déréférencement suffit).</summary>
    public static void ReleaseCache() => _toonCache.Clear();

    /// <summary>Noms de nœuds helper/collision observés dans les GLB
    /// convertis (S001/S009/S205) : « true wall/truewall » (murs de
    /// collision), « blo »/« bloc »/« 01block02 »/« 04nblock »
    /// (bloqueurs), « Box/Cylinder/Plane/Object/01 » (primitives de
    /// blocage), « camera »/« nocamera »/« 01nocamerablock08 »,
    /// « light:N » (quads glow), « Line01 » (balayages de bordure),
    /// « Bone/Biped Object/Dummy/Box@# » (helpers de rig exportés),
    /// suffixe « -s »/« -n » (variantes shadow LOD — vérifié
    /// all-untextured) et codes modèle « U[A-Z]nnn » utilisés comme
    /// bloqueurs. « Editable Poly/Mesh » est géré par
    /// <see cref="IsHelperNode"/> (fallback « Editable »). « Sphere »
    /// exclus volontairement : des dômes architecturaux portent ce nom
    /// (ub389). Regex insensible à la casse.</summary>
    private static readonly Regex HelperName = new(
        @"^(true ?wall\d*|blo(c)?|.*block\d*|.*camera\d*|knock|" +
        @"box[_@#]*\d*|floor\d*|plane\d*|object\d*|cylinder\d*|" +
        @"light\d*|line\d*|bone.*|biped.*|dummy\d*|.*nub.*|" +
        @".*-[sn]|U[A-Z]\d+([_-]\d+)?)([_:]\d+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Helpers BLOQUEURS (layer 1) : murs de collision et
    /// anti-caméra — comportement historique inchangé, étendu aux
    /// variantes préfixées « 01block02 »/« 02camera » et aux
    /// balayages « Line01 » (bordures de map — fences/containment,
    /// pas des dalles de marche).</summary>
    private static readonly Regex WallHelperName = new(
        @"^(true ?wall\d*|blo(c)?|knock|.*block\d*|.*camera\d*|" +
        @"line\d*)([_:]\d+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Helpers de blocage MEME TEXTURES : certains NIF de map
    /// (S018 « 01block01:0 », « 01Camerablock02 ») portent une texture
    /// de bâtiment sur leurs volumes de blocage — visibles comme des
    /// plans translucides une fois les textures converties. Nom strict
    /// (préfixe numérique optionnel) pour ne pas masquer un vrai décor.</summary>
    private static readonly Regex TexturedBlockerName = new(
        @"^\d*(true ?wall|blo(c)?|knock|(no)?camera ?block|block)\d*([_:]\d+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Helpers SOL MARCHABLE (layer
    /// <see cref="WalkableGround.FloorLayer"/>) : dalles explicites,
    /// primitives génériques, quads glow, variantes « -s »/« -n » et
    /// codes modèle « U[A-Z]nnn » (le pont S002 UB107/UB108 porte ses
    /// surfaces de marche sous ces noms). Ambigu possible (une « box »
    /// peut être une caisse-bloqueur) : on privilégie floor — un mur
    /// manquant est moins grave qu'un mur invisible géant ; les cas
    /// géométriquement verticaux sont logués « class=wall? » par
    /// <see cref="CollisionBuilder"/>.</summary>
    private static readonly Regex FloorHelperName = new(
        @"^(box[_@#]*\d*|floor\d*|plane\d*|object\d*|cylinder\d*|" +
        @"light\d*|bone.*|biped.*|dummy\d*|.*nub.*|" +
        @"U[A-Z]\d+([_-]\d+)?|.*-[sn])([_:]\d+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Shader ToonShader => _toonShader ??=
        GD.Load<Shader>("res://shaders/toon_lit.gdshader");
    private static Shader? _toonShader2S;
    /// <summary>Variante cull_disabled : matériaux glTF doubleSided
    /// (NiStencilProperty DRAW_BOTH — murs fins, plans de végétation).</summary>
    private static Shader ToonShader2S => _toonShader2S ??=
        GD.Load<Shader>("res://shaders/toon_lit_2s.gdshader");

    /// <summary>Applique la politique toon à tout le sous-arbre —
    /// appelé UNE FOIS sur le proto GLB dans
    /// <see cref="AssetCatalog.LoadGlbNode"/> (les Duplicate
    /// héritent).</summary>
    /// <summary>EDEN_DECOR_LEGACY=1 : désactive le masquage des meshes
    /// non texturés (capture « avant » pour comparaison).</summary>
    private static readonly bool LegacyDecor =
        System.Environment.GetEnvironmentVariable("EDEN_DECOR_LEGACY") == "1";

    public static void Apply(Node root, bool decorScope = false,
        string? modelName = null)
    {
        if (LegacyDecor) decorScope = false;
        var hasTextured = HasTexturedSurface(root);
        Apply(root, hasTextured, modelName ?? root.Name.ToString(),
            decorScope);
    }

    /// <summary>Modèle ENTIER de blocage (« nocamerablock02 »,
    /// « block01 », « floor01 »…) : aucune texture, nom de fichier de
    /// collision — son unique mesh « Box01 » est un volume invisible
    /// natif, pas un cube coloré.</summary>
    private static readonly Regex HelperModelName = new(
        @"(block|camera|floor|wall)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void Apply(Node root, bool modelHasTextured,
        string modelName, bool decorScope)
    {
        if (root is MeshInstance3D mi && mi.Mesh != null)
            ApplyToMesh(mi, modelHasTextured, modelName, decorScope);
        foreach (var child in root.GetChildren())
            Apply(child, modelHasTextured, modelName, decorScope);
    }

    /// <summary>Nom d'un helper de collision dont le NIF a TRONQUÉ le
    /// nom (« flo » = floor, « came » = camera, « ture wall »,
    /// « true wall-2 », « floor-1 », « kig kake144 » — « kake » =
    /// mur/bloqueur) : ne passait pas <see cref="HelperName"/>, donc
    /// le mesh restait visible en aplat de couleur « wire » (les
    /// murets rouges/bleus/jaunes non texturés des maps).</summary>
    private static readonly Regex TruncatedHelperName = new(
        @"^(flo|came?|.*wall.*|.*kake.*|floor.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Le modèle contient au moins une surface texturée.</summary>
    private static bool HasTexturedSurface(Node root)
    {
        if (root is MeshInstance3D mi && mi.Mesh != null)
        {
            for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                if (EffectiveMaterial(mi, i) is StandardMaterial3D sm &&
                    sm.AlbedoTexture != null)
                    return true;
        }
        foreach (var child in root.GetChildren())
            if (HasTexturedSurface(child)) return true;
        return false;
    }

    /// <summary>Vrai si le NOM du nœud correspond à un pattern
    /// helper/collision (« true wall », « blo », « Box01 »…) —
    /// utilisé par <see cref="CollisionBuilder"/> pour retrouver les
    /// meshes de collision dans les instances dupliquées.</summary>
    public static bool IsHelperByName(string name) =>
        HelperName.IsMatch(name);

    /// <summary>Classe physique d'un nœud helper (à n'appeler que sur
    /// un nœud DÉJÀ reconnu helper — meta <c>collision_helper</c>,
    /// <see cref="IsHelperByName"/> ou repli « Editable » de
    /// <see cref="IsHelperNode"/>) : <see cref="HelperClass.Wall"/> si
    /// le nom est un bloqueur explicite (y compris sa variante LOD
    /// « -s »/« -n » — « true wall-s » reste un mur), sinon
    /// <see cref="HelperClass.Floor"/> (les primitives génériques,
    /// dalles « U[A-Z]nn », « Editable Poly » et autres variantes
    /// « -s » sont des surfaces de marche ou des doublons LOD — la
    /// géométrie tranche les ambiguïtés côté
    /// <see cref="CollisionBuilder"/>).</summary>
    public static HelperClass ClassifyHelper(string name)
    {
        if (WallHelperName.IsMatch(name) ||
            Regex.IsMatch(name, @"^(came?|.*wall.*|.*kake.*)$",
                RegexOptions.IgnoreCase))
            return HelperClass.Wall;
        if (WallHelperName.IsMatch(StripLodSuffix(name)))
            return HelperClass.Wall;
        return HelperClass.Floor;
    }

    /// <summary>Nom sans suffixe LOD « -s »/« -n » (avant un
    /// éventuel « :N ») — « true wall-s » → « true wall ».</summary>
    private static string StripLodSuffix(string name)
    {
        var n = name;
        var colon = n.IndexOf(':');
        if (colon > 0) n = n[..colon];
        if (n.Length > 2 &&
            (n.EndsWith("-s", StringComparison.OrdinalIgnoreCase) ||
             n.EndsWith("-n", StringComparison.OrdinalIgnoreCase)))
            return n[..^2];
        return name;
    }

    /// <summary>Nom correspondant explicitement à la classe floor —
    /// pour le diag (le repli floor couvre aussi les noms hors
    /// liste).</summary>
    public static bool IsFloorHelperByName(string name) =>
        FloorHelperName.IsMatch(name);

    /// <summary>Vrai si le nœud porte des meshes « helper » : toutes
    /// les surfaces sans texture ET (nom de collision/helper OU le
    /// modèle a d'autres parties texturées — les nœuds « Editable
    /// Poly » sans texture dans un modèle texturé sont des dalles de
    /// collision ou des quads de LOD, jamais de la géométrie
    /// visible).</summary>
    public static bool IsHelperNode(MeshInstance3D mi,
        bool modelHasTextured)
    {
        if (!AllUntextured(mi)) return false;
        // Modèle sans AUCUNE texture (UD002, UD105…) : les surfaces
        // colorées sont le rendu voulu (déco pleine) — on garde.
        if (!modelHasTextured) return false;
        return HelperName.IsMatch(mi.Name) ||
               mi.Name.ToString().Contains("Editable");
    }

    /// <summary>Toutes les surfaces du mesh sont sans texture
    /// albedo.</summary>
    private static bool AllUntextured(MeshInstance3D mi)
    {
        for (var i = 0; i < mi.Mesh!.GetSurfaceCount(); i++)
            if (EffectiveMaterial(mi, i) is StandardMaterial3D sm &&
                sm.AlbedoTexture != null)
                return false;
        return true;
    }

    /// <summary>Vrai si le mesh descend d'un groupe « __night_ » ou
    /// « *_BB_* » (overlays billboard/nuit convertis en quads géants
    /// blancs — vérifié UD108/UD108_1 sur S002).</summary>
    private static bool IsShadowOverlay(MeshInstance3D mi)
    {
        for (Node? n = mi; n != null; n = n.GetParent())
        {
            var name = n.Name.ToString();
            if (name.Contains("__night_", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("_BB_", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Meshes sans texture restant visibles déjà logués —
    /// une entrée par modèle+mesh (chargement en tâche de fond).</summary>
    private static readonly HashSet<string> _untexturedLogged = new();

    private static bool IsCharManaged(MeshInstance3D mi)
    {
        var n = mi.Name.ToString();
        return n.Length > 2 &&
            (n[0] == '#' || (n[0] == 'S' &&
             (n[1] == 'B' || n[1] == 'G' || n[1] == 'X')));
    }

    private static void ApplyToMesh(MeshInstance3D mi,
        bool modelHasTextured, string modelName, bool decorScope)
    {
        // Meta « no_collision » (NifScene — groupes de rendu
        // « noalphanoblock »/« enhide »/« water ») : mesh de décor
        // authentique — reste visible même non texturé (les coquilles
        // « Editable Poly » géantes des salles RF sont les parois
        // pastel voulues) et n'est jamais collecté comme collider.
        var noCollision = mi.HasMeta("no_collision");
        if (IsShadowOverlay(mi))
        {
            // Quads géants « __night_ » / « thing..._BB » des modèles
            // UDxxx : overlays de nuit/ombre-billboard natifs, rendus
            // par notre pipeline comme des aplats blancs couvrant la
            // map. Ce ne sont ni de la géométrie visible ni des murs :
            // on les masque sans meta collision_helper.
            mi.Visible = false;
            mi.SetMeta("toon_hidden", "shadow_overlay");
            return;
        }
        if (!noCollision && !IsCharManaged(mi) &&
            TexturedBlockerName.IsMatch(mi.Name.ToString()))
        {
            mi.Visible = false;
            mi.SetMeta("toon_hidden", "textured_blocker");
            mi.SetMeta("collision_helper", true);
            return;
        }
        if (!noCollision && IsHelperNode(mi, modelHasTextured))
        {
            mi.Visible = false;
            mi.SetMeta("toon_hidden", "helper_node");
            mi.SetMeta("collision_helper", true);
            return;
        }
        // CAUSE RACINE des « murets rouges / éléments de décor non
        // texturés » : un mesh de props/maps SANS aucune texture dans un
        // modèle qui en a ailleurs n'est jamais de la géométrie visible
        // (le NIF n'a pas de texture parce que c'est un volume
        // helper/collision ou un overlay « Dark ») — le convertisseur
        // n'exporte pas le flag Hidden, donc son matériau « wire »
        // (rouge/bleu/jaune) s'affichait. Les noms tronqués par le NIF
        // (« flo », « came », « kake ») échappaient à HelperName. Les
        // helpers à nom de collision gardent la meta collision_helper
        // (colliders inchangés) ; les autres sont seulement masqués.
        if (decorScope && !modelHasTextured && AllUntextured(mi) &&
            HelperModelName.IsMatch(modelName))
        {
            mi.Visible = false;
            mi.SetMeta("toon_hidden", "helper_model");
            mi.SetMeta("collision_helper", true);
            return;
        }
        if (decorScope && modelHasTextured && AllUntextured(mi) &&
            !noCollision && !IsCharManaged(mi))
        {
            mi.Visible = false;
            mi.SetMeta("toon_hidden", "untextured_in_textured");
            if (TruncatedHelperName.IsMatch(mi.Name.ToString()))
                mi.SetMeta("collision_helper", true);
            return;
        }
        // Safeguard P1.1 : un mesh SANS texture resté visible dans un
        // modèle texturé = texture absente du pack (référencée par le
        // NIF mais jamais livrée) ou aplat voulu — on le logue une fois
        // par modèle pour l'audit, on ne cache pas la géométrie.
        // Les meshes « #X/SB/SG/SX » des modèles perso sont gérés par
        // CharVisual (teintes/visibilité) — ne pas les loguer ici.
        var n = mi.Name.ToString();
        var charManaged = IsCharManaged(mi);
        // Pas d'alerte sur : « no_collision » (groupe de rendu natif —
        // aplats de parois authentiques) et les nœuds masqués par le
        // NIF (flag Hidden — jamais rendus).
        if (modelHasTextured && !noCollision && mi.Visible &&
            !charManaged && AllUntextured(mi))
        {
            var key = $"{modelName}:{n}";
            var log = false;
            lock (_untexturedLogged) log = _untexturedLogged.Add(key);
            if (log)
                GD.PrintErr($"[toon] {modelName} : mesh '{n}' " +
                            "visible sans texture (texture absente du " +
                            "pack ou aplat voulu)");
        }
        for (var i = 0; i < mi.Mesh!.GetSurfaceCount(); i++)
        {
            var src = EffectiveMaterial(mi, i);
            if (src == null) continue;
            var dst = ConvertMaterial(src);
            // COLOR_0 tout noir (attribut parasite de la conversion
            // — la végétation alpha UD5xx) ne doit pas assombrir
            // l'albedo : VertexColorUseAsAlbedo désactivé au cas par
            // cas via un doublon du matériau.
            if (dst is StandardMaterial3D dsm &&
                dsm.VertexColorUseAsAlbedo &&
                !HasUsableVertexColor(mi, i))
                dst = NoVertexColorDup(dsm);
            if (!ReferenceEquals(dst, src))
                mi.SetSurfaceOverrideMaterial(i, dst);
        }
    }

    /// <summary>Vrai si la surface a un COLOR_0 non uniformément
    /// noir (les couleurs vertex toutes nulles = attribut vide
    /// écrit par la conversion, pas une teinte voulue).</summary>
    private static bool HasUsableVertexColor(MeshInstance3D mi, int surf)
    {
        var arrays = mi.Mesh!.SurfaceGetArrays(surf);
        var v = arrays[(int)Mesh.ArrayType.Color];
        var cols = v.VariantType == Variant.Type.PackedColorArray
            ? v.AsColorArray() : null;
        if (cols == null || cols.Length == 0)
            return false;
        var max = 0f;
        foreach (var c in cols)
            max = Mathf.Max(max,
                Mathf.Max(c.R, Mathf.Max(c.G, c.B)));
        return max > 0.08f;
    }

    /// <summary>Doublon de <paramref name="src"/> avec
    /// <c>VertexColorUseAsAlbedo=false</c> — cache par source.</summary>
    private static Material NoVertexColorDup(StandardMaterial3D src)
    {
        lock (_toonCache)
        {
            if (_noVcolCache.TryGetValue(src, out var d)) return d;
            var dup = (StandardMaterial3D)src.Duplicate();
            dup.VertexColorUseAsAlbedo = false;
            _noVcolCache[src] = dup;
            return dup;
        }
    }

    /// <summary>Metas posées par NifMaterials.For, recopiées sur le
    /// matériau converti (audit de rendu : texture citée vs posée).</summary>
    private static readonly string[] MatMetas = { "src_tex", "nif_tex" };

    private static readonly Dictionary<Material, Material> _noVcolCache =
        new();

    /// <summary>Matériau effectif d'une surface (override &gt;
    /// matériau du mesh).</summary>
    private static Material? EffectiveMaterial(MeshInstance3D mi, int i) =>
        mi.GetSurfaceOverrideMaterial(i) ?? mi.Mesh!.SurfaceGetMaterial(i);

    /// <summary>Conversion d'un matériau source → toon/ajusté
    /// (cache par matériau source — les GLB partagent). Le cache est
    /// accédé par les tâches de préchargement GLB en parallèle :
    /// toute la lecture/écriture et la mutation du StandardMaterial
    /// source restent sous le même verrou.</summary>
    private static Material ConvertMaterial(Material src)
    {
        lock (_toonCache)
        {
            if (_toonCache.TryGetValue(src, out var cached))
                return cached;
            Material dst;
            if (src is StandardMaterial3D sm)
            {
                var alphaish = sm.Transparency !=
                               BaseMaterial3D.TransparencyEnum.Disabled ||
                               sm.AlphaScissorThreshold > 0f;
                if (alphaish || sm.AlbedoTexture == null)
                {
                    // Feuillage/glace/couleur pleine : on garde le
                    // matériau importé, on enlève juste les réflexions.
                    sm.Metallic = 0f;
                    sm.MetallicSpecular = 0f;
                    sm.SpecularMode =
                        BaseMaterial3D.SpecularModeEnum.Disabled;
                    sm.Roughness = 1f;
                    // Vertex colors NIF (COLOR_0) = ambiant+diffus chez
                    // le natif — l'import Godot 4.7 ne les branche plus
                    // tout seul (regression has_vertex_color, issue
                    // #120625) → on force le flag : multiplie albedo par
                    // la couleur vertex (no-op si le mesh n'en a pas).
                    sm.VertexColorUseAsAlbedo = true;
                    if (alphaish)
                    {
                        sm.CullMode =
                            BaseMaterial3D.CullModeEnum.Disabled;
                        // Les props alpha NIF (voiles, cordes,
                        // feuillages) exportent souvent Emissive Color
                        // comme un bleu utilitaire — en BLEND Godot le
                        // rend comme un glow géant (UD501/S002). Les
                        // émissions réelles sont conservées par la
                        // branche opaque → shader.
                        sm.EmissionEnabled = false;
                        // Blend → alpha-hash : le vrai blend exige un
                        // tri par instance impossible sous MultiMesh
                        // (1914 objets non batchés sur S001, ~1900
                        // draws). Le hash rend des bords alpha
                        // ditherés sans tri — proche visuellement du
                        // blend natif (et de l'alpha test d'origine)
                        // et éligible au PropBatcher.
                        if (sm.Transparency == BaseMaterial3D
                                .TransparencyEnum.Alpha &&
                            sm.BlendMode == BaseMaterial3D
                                .BlendModeEnum.Mix)
                            sm.Transparency = BaseMaterial3D
                                .TransparencyEnum.AlphaHash;
                    }
                    dst = sm;
                }
                else
                {
                    var toon = new ShaderMaterial
                    {
                        Shader = sm.CullMode ==
                                 BaseMaterial3D.CullModeEnum.Disabled
                            ? ToonShader2S : ToonShader,
                    };
                    toon.SetShaderParameter("albedo_tex", sm.AlbedoTexture);
                    var c = sm.AlbedoColor;
                    toon.SetShaderParameter("modulate",
                        new Vector4(c.R, c.G, c.B, c.A));
                    // Émissif natif (Emissive Color → emissiveFactor) :
                    // recopié — lanternes/cristaux restent auto-éclairés
                    // (le shader toon ne lit pas l'émission Godot).
                    if (sm.EmissionEnabled)
                    {
                        var e = sm.Emission;
                        toon.SetShaderParameter("emission",
                            new Vector3(e.R, e.G, e.B));
                        toon.SetShaderParameter("emission_strength",
                            sm.EmissionEnergyMultiplier);
                    }
                    dst = toon;
                }
            }
            else
            {
                dst = src; // déjà ShaderMaterial (eau, ciel…) : intact
            }
            if (!ReferenceEquals(dst, src))
                foreach (var k in MatMetas)
                    if (src.HasMeta(k)) dst.SetMeta(k, src.GetMeta(k));
            _toonCache[src] = dst;
            return dst;
        }
    }
}
