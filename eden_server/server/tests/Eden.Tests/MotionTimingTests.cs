using Eden.Core.Data.Parsers;
using Xunit;

namespace Eden.Tests;

/// <summary>Tests du timing natif swing→impact (Motion.ini `hit`/`end`,
/// vol projectile) — preuves : CKfm::GetMotionTime @0x080e0b00,
/// ParseMotionIni @0x080e1cd0, CSpellToLife::GetBlastTime @0x084f7db0.</summary>
public class MotionTimingTests
{
    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"motion_{Guid.NewGuid():N}.ini");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ParseTimings_HitAndEndSeconds_ConvertedToDeciseconds()
    {
        // Format réel Motion.ini : DBn=kfm,kfId,sec,phase,link,son,nom.
        // Conversion native : round(sec*100)/10 ds → round(sec*10).
        var path = WriteTemp(
            "[KEY_Table]\n" +
            "DB1340=b,3092,0.568346,hit,NULL,,shoot51\n" +
            "DB1341=b,3092,1.234000,end,,,shoot51\n" +
            "DB7=m010,1021,0.666000,hit,NULL,,attack01\n" +
            "DB5=m010,1021,1.500000,end,,,attack01\n" +
            "DB9=x,99,2.000000,cast,NULL,,cast01\n");
        try
        {
            var t = MotionParser.ParseTimings(path);
            Assert.Equal(6, t["b"][3092].HitDs);    // 0.568346 → 6 ds
            Assert.Equal(12, t["b"][3092].EndDs);   // 1.234 → 12 ds
            Assert.Equal(7, t["m010"][1021].HitDs); // 0.666 → 7 ds
            Assert.Equal(15, t["m010"][1021].EndDs);
            Assert.False(t.ContainsKey("x"));      // phase non-hit/end
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ParseTimings_RealMotionIni_Blast3092Present()
    {
        var dir = FindDataDir();
        if (dir == null) return; // données absentes de l'env de test
        var t = MotionParser.ParseTimings(Path.Combine(dir, "Motion.ini"));
        // Capture/désasm : sort 50022 blastId 3092 — 'hit' ≈ 0.568 s.
        Assert.True(t["b"][3092].HitDs is 6);
        Assert.True(t["g"][3092].HitDs is 6);
    }

    [Fact]
    public void KfmForGender_NativeSwitch()
    {
        // CCharKfm::GetBlastID @0x080e03f0 — nibble char+0x4aa.
        Assert.Equal("g", MotionParser.KfmForGender(0));
        Assert.Equal("b", MotionParser.KfmForGender(1));
        Assert.Equal("m", MotionParser.KfmForGender(2));
        Assert.Equal("f", MotionParser.KfmForGender(4));
        Assert.Equal("k", MotionParser.KfmForGender(6));
        Assert.Equal("l", MotionParser.KfmForGender(8));
        Assert.Equal("c", MotionParser.KfmForGender(9));
        Assert.Equal("g", MotionParser.KfmForGender(3));  // défaut
        Assert.Equal("g", MotionParser.KfmForGender(99));
    }

    [Fact]
    public void BlastSwingMs_DirectKfmHit()
    {
        var t = Map(("b", 3092, 6));
        Assert.Equal(600, MotionParser.BlastSwingMs(t, "b", 3092, []));
    }

    [Fact]
    public void BlastSwingMs_CrossLetterFallback()
    {
        // Le blast n'existe que sous 'm' — le lookup 'b' retombe dessus.
        var t = Map(("m", 3092, 6));
        Assert.Equal(600, MotionParser.BlastSwingMs(t, "b", 3092, []));
    }

    [Fact]
    public void BlastSwingMs_AttackKfFallbackThenGeneric()
    {
        var t = Map(("b", 1021, 7));
        // Pas de motion blast → swing de l'attaque d'arme.
        Assert.Equal(700, MotionParser.BlastSwingMs(t, "b", 9999,
            new[] { 1021, 1022 }));
        // Rien du tout → 400 ms générique.
        Assert.Equal(400, MotionParser.BlastSwingMs(t, "b", 9999, []));
    }

    [Fact]
    public void KfHitMs_KnownAndUnknown()
    {
        var t = Map(("m010", 1021, 7));
        Assert.Equal(700, MotionParser.KfHitMs(t, "m010", 1021));
        Assert.Equal(300, MotionParser.KfHitMs(t, "m010", 9999));
    }

    [Fact]
    public void ProjectileFlyMs_DistOverSpeedAndDefault()
    {
        // GetBlastTime : 8 u à 40 u/s = 2 ds = 200 ms.
        Assert.Equal(200, MotionParser.ProjectileFlyMs(8.0, 40f));
        // Vitesse 0 → DefaultFlySpeed 40 : 20 u = 5 ds.
        Assert.Equal(500, MotionParser.ProjectileFlyMs(20.0, 0f));
        // Vitesse lue S_Spell (raw 60 → ×0.5 = 30 u/s) : 6 u = 2 ds.
        Assert.Equal(200, MotionParser.ProjectileFlyMs(6.0, 30f));
    }

    // ===== Résolution blastId — CCharKfm::GetBlastID @0x080e03f0 =====

    [Fact]
    public void ShootIndex_NativeKindMap()
    {
        // map<int,EShootActionType> d'Integration (@0x80e51xx) +
        // sélection d'arme de GetBlastID.
        Assert.Equal(0, MotionParser.ShootIndex(0x11, 0));   // épée 1H
        Assert.Equal(0, MotionParser.ShootIndex(0x18, 0));   // hache 1H
        Assert.Equal(1, MotionParser.ShootIndex(0x19, 0));   // katar
        Assert.Equal(2, MotionParser.ShootIndex(0x14, 0));   // 2H
        Assert.Equal(3, MotionParser.ShootIndex(0x16, 0));   // bâton
        Assert.Equal(4, MotionParser.ShootIndex(0x1c, 0));   // arc
        Assert.Equal(5, MotionParser.ShootIndex(0x1d, 0));   // fusil
        Assert.Equal(9, MotionParser.ShootIndex(0x21, 0));   // awoken
        Assert.Equal(12, MotionParser.ShootIndex(0x17, 0));  // lance
        Assert.Equal(0, MotionParser.ShootIndex(0x99, 0));   // inconnu → 0
        // Paire d'armes : katar en main → 10, sinon 0xb.
        Assert.Equal(10, MotionParser.ShootIndex(0x19, 0x11));
        Assert.Equal(0xb, MotionParser.ShootIndex(0x11, 0x12));
        // Désarmé → 1 ; main gauche seule.
        Assert.Equal(1, MotionParser.ShootIndex(0, 0));
        Assert.Equal(0xb, MotionParser.ShootIndex(0, 0x11));
    }

    [Fact]
    public void ResolveShootBlastId_PoolReferences()
    {
        // 412 (0x19c) → pool+0x74[idx] : shoot12 pour une 1H (idx0),
        // shoot22 (0x1a6) pour une paire de lames (idx 0xb).
        Assert.Equal(0x19c,
            MotionParser.ResolveShootBlastId(412, 0, 1));
        Assert.Equal(0x1a6,
            MotionParser.ResolveShootBlastId(412, 0xb, 1));
        // 401 (0x191) → pool+0x68 ; 3002 (0xbba) → pool+0x8c[idx].
        Assert.Equal(0x191,
            MotionParser.ResolveShootBlastId(401, 0, 1));
        Assert.Equal(0xbba,
            MotionParser.ResolveShootBlastId(3002, 0, 1));
        Assert.Equal(0xc28,
            MotionParser.ResolveShootBlastId(3002, 9, 1));
        // Index hors pool → -1 (le natif lit hors vecteur → fallback).
        Assert.Equal(-1,
            MotionParser.ResolveShootBlastId(3002, 12, 1));
    }

    [Fact]
    public void ResolveShootBlastId_DirectAndShifted()
    {
        // 3092 (0xc14) passe tel quel — la capture montre bien 0x0c14.
        Assert.Equal(0xc14,
            MotionParser.ResolveShootBlastId(3092, 0, 0));
        // Décalages natifs : fusil (idx5) → +10, faux (idx6) → 0xc37-9.
        Assert.Equal(0xc1e,
            MotionParser.ResolveShootBlastId(3092, 5, 0));
        Assert.Equal(0xc38,
            MotionParser.ResolveShootBlastId(3092, 6, 0));
        // 0xc33-0xc39 passent tels quels.
        Assert.Equal(0xc35,
            MotionParser.ResolveShootBlastId(0xc35, 0, 0));
        // Groupe haut 0xc1d/0xc1e/0xc20 : arc (idx4) → -10.
        Assert.Equal(0xc14,
            MotionParser.ResolveShootBlastId(0xc1e, 4, 0));
        Assert.Equal(0xc1e,
            MotionParser.ResolveShootBlastId(0xc1e, 0, 0));
    }

    [Fact]
    public void ResolveShootBlastId_Defaults()
    {
        // Col3 vide + power≥1 (skill physique) → -1 → l'appelant
        // applique le fallback GetAttackID (kf d'attaque — capture :
        // Onrush 50038 → 0x3fe sur le fil, jamais -1 ni 0).
        Assert.Equal(-1, MotionParser.ResolveShootBlastId(0, 0, 1));
        // power<1 (soin/buff sans dégât) → défaut pool+0x74[idx] natif.
        Assert.Equal(0x19f,
            MotionParser.ResolveShootBlastId(0, 3, 0));
        // Col3 basse-plage inconnue → défaut (pas de table).
        Assert.Equal(-1, MotionParser.ResolveShootBlastId(0x849, 0, 1));
    }

    [Fact]
    public void RideBlastRemap_NativeTable()
    {
        // LAB_080e0510 : à cheval, la col3 brute est remappée en id de
        // catégorie résolu par le KFM de la monture.
        Assert.Equal(0xbba, MotionParser.RideBlastRemap(0xbba));
        Assert.Equal(0x19c, MotionParser.RideBlastRemap(0x19c));
        Assert.Equal(0xc14, MotionParser.RideBlastRemap(0xc1e));
        Assert.Equal(0xc16, MotionParser.RideBlastRemap(0xc20));
        Assert.Equal(0x191, MotionParser.RideBlastRemap(0));
        Assert.Equal(0x191, MotionParser.RideBlastRemap(0x849));
    }

    private static Dictionary<string, Dictionary<int, MotionParser.MotionTiming>>
        Map(params (string kfm, int kf, int hitDs)[] rows)
    {
        var m = new Dictionary<string, Dictionary<int, MotionParser.MotionTiming>>();
        foreach (var (kfm, kf, hitDs) in rows)
        {
            if (!m.TryGetValue(kfm, out var inner))
                m[kfm] = inner = new Dictionary<int, MotionParser.MotionTiming>();
            inner[kf] = new MotionParser.MotionTiming(hitDs, 0);
        }
        return m;
    }

    private static string? FindDataDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var c = Path.Combine(dir.FullName, "server_us/Data/db_decoded");
            if (File.Exists(Path.Combine(c, "Motion.ini"))) return c;
            dir = dir.Parent;
        }
        return null;
    }
}
