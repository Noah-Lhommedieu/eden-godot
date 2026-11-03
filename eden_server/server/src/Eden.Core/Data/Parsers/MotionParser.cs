using System.Collections.Generic;

namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parser pour Motion.ini — tables de motions KF par modèle.
///
/// Le fichier contient deux sections avec des lignes `DBn=` :
///   [KEY_INFO]  : `DB1=m010,1021,4,attack01` (modèle,kfId,vitesse,nom)
///   [KEY_Table] : `DB186=m014,1021,0.405334,hit,NULL,,attack01`
///                 (modèle,kfId,frametime,phase,link,son,nom)
///
/// Ce qui nous intéresse : pour chaque préfixe de modèle (m010, m025…),
/// la liste des kfId dont le nom commence par "attack" — c'est le pool
/// que CMonsterKfm::GetAttackID (désasm @0x080e1010) pioche par
/// `random() % count` pour remplir le field5 du 272 ClientMeleeAttack.
/// Le kfId 1021 (0x3fd) est le défaut natif quand le KFM n'a aucune
/// attaque configurée — on garde ce repli.
/// </summary>
public static class MotionParser
{
    /// <summary>kfId d'attaque natif quand le modèle n'a aucune entrée
    /// attack* — `*param_2 = 0x3fd` dans CMonsterKfm::GetAttackID.</summary>
    public const int DefaultAttackKfId = 1021;

    /// <summary>
    /// Charge `Motion.ini` et renvoie préfixe de modèle (minuscules,
    /// ex. "m025") -> kfIds d'attaque distincts triés.
    /// Les deux sections sont lues — le nom d'animation est toujours le
    /// dernier champ de la ligne.
    /// </summary>
    public static Dictionary<string, int[]> ParseAttackIds(string filePath)
    {
        var map = new Dictionary<string, SortedSet<int>>();
        foreach (var line in System.IO.File.ReadLines(filePath))
        {
            // Lignes `DBn=…` uniquement — on ignore en-têtes/commentaires.
            var eq = line.IndexOf('=');
            if (eq < 0 || !line.StartsWith("DB")) continue;
            var cols = line[(eq + 1)..].Split(',');
            if (cols.Length < 4) continue;
            var model = cols[0].Trim().ToLowerInvariant();
            if (!int.TryParse(cols[1].Trim(), out var kfId)) continue;
            var name = cols[^1].Trim();
            if (!name.StartsWith("attack",
                    System.StringComparison.OrdinalIgnoreCase)) continue;
            if (!map.TryGetValue(model, out var set))
                map[model] = set = new SortedSet<int>();
            set.Add(kfId);
        }
        var result = new Dictionary<string, int[]>();
        foreach (var (k, v) in map) result[k] = System.Linq.Enumerable.ToArray(v);
        return result;
    }

    /// <summary>Timing d'une motion (KFM,kfId) en décisecondes —
    /// unité native des CEvent (CEventSpellBlasting arme en ds,
    /// désasm @0x0866d6e0).</summary>
    /// <param name="HitDs">Temps de la clef `hit` — le moment où le
    /// projectile/l'impact part (CKfm::GetMotionTime @0x080e0b00).</param>
    /// <param name="EndDs">Temps de la clef `end` — durée totale de
    /// l'anim (renvoyé dans le field5 des paquets natifs).</param>
    public readonly record struct MotionTiming(int HitDs, int EndDs);

    /// <summary>
    /// Charge `Motion.ini` et renvoie `kfm -> kfId -> (hitDs, endDs)`
    /// pour les lignes dont la phase est `hit` ou `end`.
    /// Conversion native (ParseMotionIni @0x080e1cd0) : `sec` float →
    /// `round(sec*100)/10` ds — soit `round(sec*10)` ds (0.568346 s
    /// -> 6 ds pour la motion b,3092 de la capture).
    /// </summary>
    public static Dictionary<string, Dictionary<int, MotionTiming>>
        ParseTimings(string filePath)
    {
        var acc = new Dictionary<string, Dictionary<int, int[]>>();
        foreach (var line in System.IO.File.ReadLines(filePath))
        {
            var eq = line.IndexOf('=');
            if (eq < 0 || !line.StartsWith("DB")) continue;
            var cols = line[(eq + 1)..].Split(',');
            if (cols.Length < 4) continue;
            var phase = cols[3].Trim();
            var slot = phase.Equals("hit",
                System.StringComparison.OrdinalIgnoreCase) ? 0
                : phase.Equals("end",
                    System.StringComparison.OrdinalIgnoreCase) ? 1 : -1;
            if (slot < 0) continue;
            var model = cols[0].Trim().ToLowerInvariant();
            if (!int.TryParse(cols[1].Trim(), out var kfId) ||
                !double.TryParse(cols[2].Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var sec)) continue;
            if (!acc.TryGetValue(model, out var perKf))
                acc[model] = perKf = new Dictionary<int, int[]>();
            if (!perKf.TryGetValue(kfId, out var pair))
                perKf[kfId] = pair = new int[2];
            pair[slot] = (int)System.Math.Round(sec * 10.0);
        }
        var result = new Dictionary<string, Dictionary<int, MotionTiming>>();
        foreach (var (kfm, perKf) in acc)
        {
            var inner = new Dictionary<int, MotionTiming>();
            foreach (var (kf, t) in perKf)
                inner[kf] = new MotionTiming(t[0], t[1]);
            result[kfm] = inner;
        }
        return result;
    }

    /// <summary>`SSpellData::DefaultFlySpeed` natif (.data@0x089abae2)
    /// — 40 u/s, repli quand S_Spell col9 = 0.</summary>
    public const float DefaultFlySpeed = 40f;

    /// <summary>Nibble genre→lettre KFM du perso — switch natif de
    /// `CCharKfm::GetBlastID` zone@0x080e03f0 sur `char+0x4aa` :
    /// {0:g, 1:b, 2:m, 4:f, 6:k, 8:l, 9:c, défaut:g}. La lettre
    /// désigne le KFM corps (b/g/f/k/l/c/m) dans lequel vivent les
    /// motions de blast/attaque du perso.</summary>
    public static string KfmForGender(int genderId) => genderId switch
    {
        0 => "g",
        1 => "b",
        2 => "m",
        4 => "f",
        6 => "k",
        8 => "l",
        9 => "c",
        _ => "g",
    };

    /// <summary>Temps `hit` d'une motion en ds (0 si absente).</summary>
    private static bool TryHitDs(
        IReadOnlyDictionary<string, Dictionary<int, MotionTiming>> t,
        string kfm, int kfId, out int ds)
    {
        ds = 0;
        if (t.TryGetValue(kfm, out var perKf) &&
            perKf.TryGetValue(kfId, out var timing))
            ds = timing.HitDs;
        return ds > 0;
    }

    /// <summary>Délai swing d'un sort en ms — temps `hit` de la motion
    /// `blastId` (S_Spell c4 → SSpellData+0xa0) dans le KFM `kfm`.
    /// Chaîne de repli : blast dupliqué entre lettres dans Motion.ini →
    /// temps `hit` de l'attaque d'arme (GetAttackID — les skills sans
    /// blast group rejouent le swing d'arme) → 400 ms générique.</summary>
    public static int BlastSwingMs(
        IReadOnlyDictionary<string, Dictionary<int, MotionTiming>> t,
        string kfm, int blastId, IReadOnlyList<int> attackKfIds)
    {
        if (blastId > 0)
        {
            if (TryHitDs(t, kfm, blastId, out var ds)) return ds * 100;
            foreach (var alt in new[] { "g", "m", "f", "b", "k", "l", "c" })
                if (TryHitDs(t, alt, blastId, out ds))
                    return ds * 100;
        }
        if (attackKfIds.Count > 0 &&
            TryHitDs(t, kfm, attackKfIds[0], out var ads))
            return ads * 100;
        return 400; // ≈ hit moyen constaté
    }

    /// <summary>Temps `hit` de la motion `kfId` en ms (attaque arme)
    /// — 300 ms si la motion est inconnue.</summary>
    public static int KfHitMs(
        IReadOnlyDictionary<string, Dictionary<int, MotionTiming>> t,
        string kfm, int kfId) =>
        TryHitDs(t, kfm, kfId, out var ds) ? ds * 100 : 300;

    /// <summary>Temps de vol d'un projectile en ms —
    /// `CSpellToLife::GetBlastTime` @0x084f7db0 : distance 3D /
    /// flySpeed (0 → DefaultFlySpeed 40 u/s), arrondi à la ds.</summary>
    public static int ProjectileFlyMs(double dist, float flySpeed)
    {
        var speed = flySpeed > 0 ? flySpeed : DefaultFlySpeed;
        return (int)Math.Round(dist / speed * 10.0) * 100;
    }

    /// <summary>Préfixe Motion.ini du code modèle S_Monster —
    /// `M025010` -> `m025` (4 premiers caractères en minuscules).</summary>
    public static string ModelPrefix(string modelCode) =>
        string.IsNullOrEmpty(modelCode) ? string.Empty
            : modelCode[..System.Math.Min(4, modelCode.Length)]
                .ToLowerInvariant();

    /// <summary>
    /// Pools d'attaque du JOUEUR — table native extraite de
    /// `CCharKfm::Integration` @0x80e09e0 (zone/decompiled.c) : le
    /// dispatch sur le kfId remplit trois tableaux de pools, puis la
    /// map kind→pool lie chaque kind d'arme (S_Item col8 →
    /// CItemData+0x20) a un pool. Contenus (dispatch kfId→pool) :
    ///   A[0] = {1021,1022} attack01/02 — 1H mêlée (kfId 0x3fd/0x3fe)
    ///   A[1] = {1023,1024} attack03/04 — hache/masse (0x3ff/0x400)
    ///   A[2] = {1121,1122} attack05/06 — poing (0x461/0x462)
    ///   B[0] = {1221,1222} attack08/09 — 2H (0x4c5/0x4c6)
    ///   B[1] = {1321,1322} attack10/11 — bâton (0x529/0x52a)
    ///   B[2] = {1421}      attack12 = bow1 (0x58d)
    ///   B[3] = {1521}      attack13 = gun1 (0x5f1)
    ///   B[4] = {1621}      attack14        (0x655)
    ///   B[5] = {1721}      attack15        (0x6b9)
    ///   B[6] = {1821}      attack16        (0x71d)
    ///   B[7] = {2221,2222} attack25/26     (0x8ad-0x8af)
    ///   B[8] = {2321}      attack27        (0x911)
    ///   désarmé (this+0x20) = {1121,1122,2121} (0x461/0x462/0x849)
    /// Kinds S_Item col8 ↔ armes (noms Big5) : 17-19 = 1H mêlée
    /// (dague/épée), 20-23 = 2H, 24 = sceptre 1H, 25/26 = poings,
    /// 27 = masse 2H, 28 = ARC, 29 = gun, 30 = ?, 31 = harpe,
    /// 32 = bâton, 33 = sceptre magique, 34 = bouclier.
    /// </summary>
    private static readonly Dictionary<int, int[]> WeaponAttackKfIds = new()
    {
        // 0x11/0x12/0x13/0x3d → A[0] : épées/dagues 1H
        [0x11] = new[] { 1021, 1022 },
        [0x12] = new[] { 1021, 1022 },
        [0x13] = new[] { 1021, 1022 },
        [0x3d] = new[] { 1021, 1022 },
        // 0x18/0x1a → A[1] : haches/masses 1H
        [0x18] = new[] { 1023, 1024 },
        [0x1a] = new[] { 1023, 1024 },
        // 0x19/0x3e → A[2] : poings/katar
        [0x19] = new[] { 1121, 1122 },
        [0x3e] = new[] { 1121, 1122 },
        // 0x14/0x15/0x41 → B[0] : armes 2H
        [0x14] = new[] { 1221, 1222 },
        [0x15] = new[] { 1221, 1222 },
        [0x41] = new[] { 1221, 1222 },
        // 0x16/0x1b → B[1] : bâtons/sceptres
        [0x16] = new[] { 1321, 1322 },
        [0x1b] = new[] { 1321, 1322 },
        // 0x1c/0x3f → B[2] : arc (1421 = attack12 "bow1")
        [0x1c] = new[] { 1421 },
        [0x3f] = new[] { 1421 },
        // 0x1d/0x40 → B[3] : fusil/gun
        [0x1d] = new[] { 1521 },
        [0x40] = new[] { 1521 },
        // 0x1f/0x42 → B[4] : artillerie/canon
        [0x1f] = new[] { 1621 },
        [0x42] = new[] { 1621 },
        // 0x20 → B[5] : instrument (harpe)
        [0x20] = new[] { 1721 },
        // 0x21 → B[6] : arme awoken
        [0x21] = new[] { 1821 },
        // 0x17 → B[7] : rapière/lance
        [0x17] = new[] { 2221, 2222 },
        // 0x1e → B[8] : faux/grimoire
        [0x1e] = new[] { 2321 },
    };

    /// <summary>Pools de COMBO a deux armes — tableau `this+0x38` du
    /// CCharKfm natif : 9 pools indexés [classeMain×3 + classeOff] ;
    /// classes : 0 = blade (0x11-0x13/0x3d), 1 = axe (0x18/0x1a),
    /// 2 = autre. Contenus natifs (meme dispatch Integration) :
    ///   C[0] blade×blade = {2021,2022} attack17/18 (kfId 0x7e5/0x7e6)
    ///   C[4] axe×axe     = {2023,2024} attack19/20 (0x7e7/0x7e8)
    ///   C[8] autre×autre = {1921,1922} attack21/22 (0x781/0x782)
    ///   C[1,2,3,5,6,7]   = {2025,2026} attack23/24 (0x7e9/0x7ea sont
    ///     poussés dans les six pools mixtes).
    /// C'est ce pool qu'attend un voleur a deux lames — sans lui le
    /// natif ne joue pas les anims des deux bras.</summary>
    private static readonly int[][] DualAttackKfIds =
    {
        new[] { 2021, 2022 },   // C[0] blade×blade
        new[] { 2025, 2026 },   // C[1] blade×axe
        new[] { 2025, 2026 },   // C[2] blade×autre
        new[] { 2025, 2026 },   // C[3] axe×blade
        new[] { 2023, 2024 },   // C[4] axe×axe
        new[] { 2025, 2026 },   // C[5] axe×autre
        new[] { 2025, 2026 },   // C[6] autre×blade
        new[] { 2025, 2026 },   // C[7] autre×axe
        new[] { 1921, 1922 },   // C[8] autre×autre
    };

    /// <summary>Pool désarmé (aucun kind résolu) — vec@0x20 du KFM perso
    /// natif : attack05/attack06/attack21 (1121/1122/2121).</summary>
    private static readonly int[] UnarmedAttackKfIds = { 1121, 1122, 2121 };

    /// <summary>Classe d'arme pour la matrice dual : 0=blade
    /// (0x11-0x13/0x3d), 1=axe (0x18/0x1a), 2=autre.</summary>
    private static int WeaponClass(int kind) =>
        kind is >= 0x11 and <= 0x13 or 0x3d ? 0 :
        kind is 0x18 or 0x1a ? 1 : 2;

    /// <summary>
    /// Pool de kfIds d'attaque du joueur pour un kind d'arme S_Item
    /// (col 8) — arme seule (map kind→pool native). Retourne le pool
    /// désarmé si le kind n'est pas une arme connue.
    /// Preuve : CCharKfm::Integration @0x80e09e0.
    /// </summary>
    public static IReadOnlyList<int> PlayerAttackKfIds(int weaponKind) =>
        WeaponAttackKfIds.TryGetValue(weaponKind, out var pool)
            ? pool : UnarmedAttackKfIds;

    /// <summary>Pool de kfIds pour une PAIRE d'armes — matrice dual
    /// C[main×3+off] native. `offKind &lt;= 0` (rien en main gauche ou
    /// bouclier) retombe sur le pool single-weapon, comme le natif
    /// (GetWeapon retourne un seul clone → map par kind).</summary>
    public static IReadOnlyList<int> PlayerAttackKfIds(int mainKind,
        int offKind)
    {
        if (offKind <= 0)
            return PlayerAttackKfIds(mainKind);
        return DualAttackKfIds[WeaponClass(mainKind) * 3
            + WeaponClass(offKind)];
    }

    // ===== Résolution du blastId wire — CCharKfm::GetBlastID =====
    // (@0x080e03f0, zone/decompiled.c l.124751). La col3 S_Spell
    // (SSpellData+0xa0) n'est quasiment jamais un kf direct pour les
    // skills physiques : 401=pool+0x68, 412=pool+0x74, 3001-3004 =
    // pools +0x80/+0x8c/+0x98/+0xa4, tous indexés par l'index « shoot »
    // de la classe d'arme ; 3091/3092/3094 (0xc13-0xc16) passent avec
    // décalages par arme ; vide → -1 → fallback GetAttackID
    // (@0x080e0a70, vfunc+8) = kfId d'attaque de l'arme. Le natif
    // n'envoie JAMAIS -1 ni la référence brute sur le fil (captures
    // golden : Onrush 50038 → 0x3fe, sort 50022 → 0xc14).

    /// <summary>Index « shoot » (iVar8 natif) par kind d'arme S_Item
    /// col8 — `map&lt;int,EShootActionType&gt;` remplie par les
    /// `insert(local_32c)` de `CCharKfm::Integration` (@0x80e51xx,
    /// map à this+0xb0) : 1H mêlée → 0, poing/katar → 1, 2H → 2,
    /// bâton/sceptre → 3, arc → 4, fusil → 5, faux → 6, artillerie → 7,
    /// harpe → 8, awoken → 9, lance → 12.</summary>
    private static readonly IReadOnlyDictionary<int, int>
        ShootIndexByKind = new Dictionary<int, int>
    {
        [0x11] = 0, [0x12] = 0, [0x13] = 0, [0x18] = 0, [0x1a] = 0,
        [0x3d] = 0,
        [0x19] = 1, [0x3e] = 1,
        [0x14] = 2, [0x15] = 2, [0x41] = 2,
        [0x16] = 3, [0x1b] = 3,
        [0x1c] = 4, [0x3f] = 4,
        [0x1d] = 5, [0x40] = 5,
        [0x1e] = 6,
        [0x1f] = 7, [0x42] = 7,
        [0x20] = 8,
        [0x21] = 9,
        [0x17] = 12,
    };

    /// <summary>iVar8 de `GetBlastID` @0x080e03f0 — sélection de la
    /// colonne pool selon l'armement : main seule →
    /// <see cref="ShootIndexByKind"/> (kind absent → 0) ; deux armes →
    /// 10 si la main = katar 0x19, sinon 0xb ; main gauche seule → 10
    /// si 0x19 sinon 0xb ; désarmé → 1.</summary>
    public static int ShootIndex(int mainKind, int offKind)
    {
        if (mainKind == 0)
            return offKind == 0 ? 1 : offKind == 0x19 ? 10 : 0xb;
        if (offKind == 0)
            return ShootIndexByKind.TryGetValue(mainKind, out var idx)
                ? idx : 0;
        return mainKind == 0x19 ? 10 : 0xb;
    }

    // Pools blast littéraux — pushes de `CCharKfm::Integration`
    // (zone/decompiled.c @0x80e51xx-0x80e59xx ; vectors this+0x68,
    // +0x74, +0x80, +0x8c, +0x98, +0xa4). Contenus = kfIds « shootNN »
    // de Motion.ini du KFM perso.
    private static readonly int[] BlastPool68 =   // col3 = 0x191 (401)
        { 0x191, 0x192, 0x193, 0x194, 0x195, 0x196, 0x197, 0x198,
          0x199, 0x19a, 0x19b, 0x1a7 };
    private static readonly int[] BlastPool74 =   // col3 = 0x19c (412)
        { 0x19c, 0x19d, 0x19e, 0x19f, 0x1a0, 0x1a1, 0xc32, 0x1a2,
          0x1a3, 0x1a4, 0x1a5, 0x1a6, 0x1a8 };
    private static readonly int[] BlastPool80 =   // col3 = 0xbb9 (3001)
        { 0xbb9, 0xbc3, 0xbcd, 0xbd7, 0xbe1, 0xbeb, 0xbf5, 0xbff,
          0xc09, 0xc27 };
    private static readonly int[] BlastPool8c =   // col3 = 0xbba (3002)
        { 0xbba, 0xbc4, 0xbce, 0xbd8, 0xbe2, 0xbec, 0xbf6, 0xc00,
          0xc0a, 0xc28 };
    private static readonly int[] BlastPool98 =   // col3 = 0xbbb (3003)
        { 0xbbb, 0xbc5, 0xbcf, 0xbd9, 0xbe3, 0xbed, 0xbf7, 0xc01,
          0xc0b, 0xc29 };
    private static readonly int[] BlastPoolA4 =   // col3 = 0xbbc (3004)
        { 0xbbc, 0xbc6, 0xbd0, 0xbda, 0xbe4, 0xbee, 0xbf8, 0xc02,
          0xc0c, 0xc2a };

    /// <summary>Lecture bornée d'un pool blast — un index hors taille
    /// lit de la mémoire non initialisée côté natif (reserve 0xd sans
    /// push) → renvoyer -1 pour que l'appelant retombe sur le fallback
    /// attaque.</summary>
    private static int PoolAt(int[] pool, int idx) =>
        (uint)idx < (uint)pool.Length ? pool[idx] : -1;

    /// <summary>Dispatch `GetBlastID` hors monture : traduit la col3
    /// S_Spell (+0xa0) en kfId concret pour l'index d'arme
    /// <paramref name="shootIdx"/>. <paramref name="damagePower"/> =
    /// SSpellData+0xc0 (S_Spell col35) — quand &lt; 1 (soins/buffs sans
    /// dégât) le défaut natif est pool+0x74[idx] au lieu de -1.
    /// Retourne -1 quand rien ne résout → l'appelant applique le
    /// fallback `GetAttackID` (motion d'attaque de l'arme).</summary>
    public static int ResolveShootBlastId(int col3, int shootIdx,
        int damagePower)
    {
        var id = damagePower < 1 ? PoolAt(BlastPool74, shootIdx) : -1;
        if (col3 == 0xbbc) return PoolAt(BlastPoolA4, shootIdx);
        if (col3 >= 0xbbc)
        {
            // Haute plage — ids concrets avec décalages par arme.
            if (col3 < 0xc1f)
            {
                if (col3 > 0xc1c)
                    return ShiftedHighBlast(col3, shootIdx);  // c1d/c1e
                if (col3 > 0xc12 && (col3 < 0xc15 || col3 == 0xc16))
                {
                    // {0xc13,0xc14,0xc16} — fusil (idx5) → +10,
                    // faux (idx6) → 0xc37/0xc38/0xc39.
                    id = col3;
                    if (shootIdx == 5) return id + 10;
                    if (shootIdx == 6)
                        return col3 switch
                        {
                            0xc13 => 0xc37, 0xc14 => 0xc38,
                            _ => 0xc39,
                        };
                    return id;
                }
                return id;
            }
            if (col3 == 0xc20) return ShiftedHighBlast(col3, shootIdx);
            if (col3 > 0xc1f && col3 - 0xc33 < 7)
                return col3;                       // 0xc33..0xc39
            return id;
        }
        // Basse plage — références de pool / spéciaux.
        if (col3 is 0x25b or 0x25d) return col3;   // +param_4=1 natif
        if (col3 < 0x25e)
        {
            if (col3 == 0x19c) return PoolAt(BlastPool74, shootIdx);
            if (col3 == 0x191) return PoolAt(BlastPool68, shootIdx);
            return id;
        }
        if (col3 == 0xbba) return PoolAt(BlastPool8c, shootIdx);
        if (col3 == 0xbb9) return PoolAt(BlastPool80, shootIdx);
        if (col3 >= 0xbbb) return PoolAt(BlastPool98, shootIdx);
        return id;
    }

    /// <summary>LAB_080e0820 — ids 0xc1d/0xc1e/0xc20 : l'arc (idx4)
    /// lit la variante -10 (0xc13/0xc14/0xc16), la faux (idx6) les
    /// 0xc37/0xc38/0xc39 ; les autres classes gardent l'id brut.</summary>
    private static int ShiftedHighBlast(int col3, int shootIdx)
    {
        if (shootIdx == 4) return col3 - 10;
        if (shootIdx == 6)
            return col3 switch
            {
                0xc1d => 0xc37, 0xc1e => 0xc38, _ => 0xc39,
            };
        return col3;
    }

    /// <summary>Remap monture — LAB_080e0510 de `GetBlastID` : à cheval
    /// (char+0x3b bit2 &&amp; char+0x824 ≠ 0) la <b>col3 brute</b> est
    /// convertie en id de catégorie — le client résout alors dans le
    /// KFM de la monture : 0xbb9-0xbbc et 0x19c passent tels quels,
    /// 0xc13/0xc1d → 0xc13, 0xc14/0xc1e → 0xc14, 0xc16/0xc20 → 0xc16,
    /// tout le reste → 0x191.</summary>
    public static int RideBlastRemap(int col3) => col3 switch
    {
        0xbb9 or 0xbba or 0xbbb or 0xbbc or 0x19c => col3,
        0xc13 or 0xc1d => 0xc13,
        0xc14 or 0xc1e => 0xc14,
        0xc16 or 0xc20 => 0xc16,
        _ => 0x191,
    };
}
