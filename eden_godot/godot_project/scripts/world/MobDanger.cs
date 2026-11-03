using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;

namespace EdenClient.World;

/// <summary>
/// Couleur du nom d'un mob selon le danger relatif au joueur (2.5).
/// Règles portées du serveur (ZoneEntryFlow.Combat.Aggro — natif
/// CAI::DoSeekEnemy + CheckLevel) :
///  - GRIS : le joueur dépasse le mob d'au moins 8 niveaux
///    (CheckLevel = 100/S_Param[40] + S_Param[41] = 8, zone @0x84d6f0) —
///    trop faible, sans danger ;
///  - ROUGE : mob qui peut m'aggro (S_Monster col37 rayon &gt; 0, profil
///    c_ai col2 « chance de détection » &gt; 0, pas une entité décor) ;
///  - JAUNE CLAIR : mob attaquable mais non agressif (reste dangereux
///    si on le provoque).
/// </summary>
public static class MobDanger
{
    /// <summary>Seuil natif d'écart de niveau (mob « trop bas »).</summary>
    public const int LowLevelGap = 8;

    public static readonly Color Red = new(1f, 0.28f, 0.25f);
    public static readonly Color LightYellow = new(1f, 0.97f, 0.62f);
    public static readonly Color Gray = new(0.72f, 0.72f, 0.72f);

    /// <summary>Résolution c_monster (injectée par GameScene).</summary>
    public static Func<int, MonsterTable.MonsterDef?>? Lookup;

    private static int _playerLevel;
    public static event Action? LevelChanged;
    public static int PlayerLevel
    {
        get => _playerLevel;
        set
        {
            if (value == _playerLevel) return;
            _playerLevel = value;
            LevelChanged?.Invoke();
        }
    }

    private static Dictionary<int, int>? _seek;

    private static int SeekChance(int aiId)
    {
        if (_seek == null)
        {
            _seek = new();
            try
            {
                var path = AssetCatalog.ResolveRawAsset("data/db/c_ai.ini");
                if (path != null)
                    foreach (var row in IniTable.ParseFile(path,
                                 EdenEncoding.Big5).Rows)
                        if (row.Length > 2 &&
                            int.TryParse(row[0], out var id))
                            _seek[id] = IniTable.GetInt(row, 2);
            }
            catch (System.Exception e)
            {
                GD.PrintErr($"[danger] c_ai : {e.Message}");
            }
        }
        return _seek.GetValueOrDefault(aiId);
    }

    public enum Kind { Unknown, LowLevel, Aggressive, Passive }

    public static Kind Classify(int monsterId)
    {
        var def = Lookup?.Invoke(monsterId);
        if (def == null) return Kind.Unknown;
        // Élite/boss (MonsterType ≥ 2) : toujours rouge (A6.1).
        if (def.IsEliteOrBoss) return Kind.Aggressive;
        if (_playerLevel <= 0) return Kind.Unknown;
        if (_playerLevel - def.Level >= LowLevelGap) return Kind.LowLevel;
        var structure = def.AttackPower <= 1 || def.AttackRangeMax <= 0
            || def.AiId == 164;
        if (!structure && def.AggroCells > 0 && def.AiId > 0 &&
            SeekChance(def.AiId) > 0)
            return Kind.Aggressive;
        return Kind.Passive;
    }

    public static Color? ColorOf(int monsterId) => Classify(monsterId) switch
    {
        Kind.LowLevel => Gray,
        Kind.Aggressive => Red,
        Kind.Passive => LightYellow,
        _ => null,
    };
}
