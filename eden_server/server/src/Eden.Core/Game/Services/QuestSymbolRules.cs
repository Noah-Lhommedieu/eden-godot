namespace Eden.Core.Game.Services;

/// <summary>
/// Symboles de quête au-dessus des PNJ (S→C 345 UpdateNPCSymbols, relais
/// du ZMis 65 : wire natif <c>CNC_ZMis_ZoneServerUpdateNPCSymbols::
/// Deserialize</c> mission @0x081192e0 = [i32 charUid][s16 node][u16 n]
/// [n×(i32 npcUid, s16 sym)]). Le natif ne documente pas la valeur
/// exacte des symboles (la zone ignore le ZMis, <c>CNE_ZMis_…::Execute</c>
/// @0x0833eda0 est vide ; le client natif dessine selon la valeur) ;
/// <c>CFileDataFactory::SetMissionListTrigger</c> @0x0805f300 NE traite
/// PAS les symboles (construit seulement les listes NeededOpenMission/
/// NeededFinishMission). Convention retenue — alignée sur le champ
/// FMS que le dialogue natif met dans l'icône du choix (0=nouvelle,
/// 4=à rendre, 3=en cours) et sur le rendu client existant
/// (MissionBridge.SetSymbolMarker) : 1 = « ! » quête disponible,
/// 2 = « ? » à rendre, 3 = « ? » gris en cours. (HYPOTHESIS pour les
/// valeurs ; priorité 2 &gt; 1 &gt; 3 quand un PNJ cumule.)
/// </summary>
public static class QuestSymbolRules
{
    public const short Available = 1, Ready = 2, InProgress = 3;

    /// <summary>Priorité d'affichage : à rendre &gt; disponible &gt; en cours.</summary>
    private static int Rank(short s) => s switch
    {
        Ready => 3, Available => 2, InProgress => 1, _ => 0,
    };

    /// <summary>Garde le symbole le plus prioritaire pour un PNJ.</summary>
    public static short Merge(short current, short candidate) =>
        Rank(candidate) > Rank(current) ? candidate : current;

    /// <summary>Ajoute un candidat au dictionnaire npc → symbole.</summary>
    public static void Add(IDictionary<int, short> map, int npcUid,
        short sym)
    {
        if (npcUid <= 0 || sym == 0) return;
        map[npcUid] = Merge(map.TryGetValue(npcUid, out var cur) ? cur
            : (short)0, sym);
    }
}
