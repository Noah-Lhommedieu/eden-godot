using Godot;

namespace EdenClient.Data;

/// <summary>Pré-chauffe les tables INI lourdes pendant l'écran de
/// chargement (tâche de fond unique — évite le parse synchrone de
/// c_spell ~60k lignes / t_item ~9 Mo au premier open du panneau
/// Classes). Séquentiel : les tables partagent leurs verrous de
/// chargement. Pur .NET, aucune mutation de scène.</summary>
public static class DataWarmup
{
    private static int _started;

    public static void Run()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        var t = Perf.PerfTrace.Mark();
        try
        {
            _ = TextResolver.ClassName(1);        // T_Class.ini
            _ = TextResolver.SpellName(1);        // T_Spell.ini
            _ = TextResolver.ItemName(1);         // t_item + t_itemmall
            _ = TextResolver.ItemDescription(1);
            Hud.ClassWindow.WarmTalents();        // c_talent.ini
            foreach (var s in Combat.SpellCatalog.ClassSpells(1))
                _ = s;                            // c_spell.ini (~60k)
            _ = Combat.SpellCatalog.CastDs(1);
            _ = Tables.SpellRefTable.MaxLevel;    // C_SpellReference
            _ = Tables.EnchantTable.Get(1);       // c_enchant + t_enchant
            _ = Screens.CharSelectScreen.ClassIcon(1); // c_class + uiicon
        }
        catch (Exception e)
        {
            GD.PrintErr($"[warmup] {e.Message}");
        }
        Perf.PerfTrace.Report("warmup tables", t);
    }
}
