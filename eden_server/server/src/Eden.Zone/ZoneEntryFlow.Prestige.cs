using Eden.Core.Logging;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Prestige côté zone — miroir de la map <c>CCharacter+0x6c4</c>
/// native (clé = prestige_id, valeur = SPrestigeData {s16 level,
/// i32 exp}).
///
/// Chaîne native reversée :
/// - La mission détient la table <c>prestige</c> et applique
///   <c>GainPrestigeExp</c> (@0x0808c1b0 — clamp [0, S_Parameter 361 =
///   75000], level forcé à 1) puis broadcast
///   <c>CNC_MW_WorldServerUpdatePrestigeData</c> au monde.
/// - Le monde (<c>CNE_MW_WorldServerUpdatePrestigeData::Execute</c>
///   @0x0812bff0) émet NCID 60 au client et le texte
///   <c>update_prestige &lt;ch&gt; &lt;node&gt; &lt;id&gt; &lt;lv&gt;
///   &lt;exp&gt;</c> à la zone hébergeant le perso — reçu ici via
///   <c>Server_TC_UpdatePrestige</c> (@0x083b9930) →
///   <c>CCharacter::UpdatePrestige</c> (@0x08419ee0).
/// - Les gains côté zone montent par
///   <c>CPlayerFactory::AddPrestigeExp</c> (@0x080f3120) → texte DBA
///   <c>"GainExp Prestige %hd = %d"</c> → mission ; chez nous la zone
///   émet <c>gain_prestige</c> sur le lien monde (ZW 636) et le monde
///   rejoue le relais (60 client + update_prestige retour).
///
/// Le miroir sert aux gates <c>GetPrestigeLevel</c>/
/// <c>GetPrestigeExp</c> (@0x084096d0/0x08409740) : battlefield,
/// pay events, conditions de missions (<c>If_PrestigeExp</c> —
/// missiondata+0x7c/0x80).
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Point d'entrée des commandes texte monde→zone
    /// (ZW 23 — <c>CNC_ZW_ZoneServerTextCommand</c>) : routé sur la
    /// boucle, résout le perso cible puis applique le verbe connu —
    /// aujourd'hui <c>update_prestige</c>.</summary>
    public Task HandleWorldTextCommandAsync(string text)
        => RunOnLoopAsync(async _ =>
        {
            var t = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 2) return;
            switch (t[0])
            {
                case "update_prestige":
                    // update_prestige <ch_id> <node_id> <prestige_id>
                    // <level> <exp> — 5 args natifs (node lu mais
                    // ignoré par Server_TC_UpdatePrestige @0x083b9930).
                    if (t.Length >= 6
                        && int.TryParse(t[1], out var charId)
                        && short.TryParse(t[3], out var pid)
                        && short.TryParse(t[4], out var level)
                        && int.TryParse(t[5], out var exp)
                        && _services.Sessions.FindByCharacter(charId)
                            is { } ps
                        && _states.TryGetValue(ps.SessionId, out var pst))
                        pst.Prestige[pid] = (level, exp);
                    break;
            }
        });

    /// <summary>Exp prestige courante du perso pour un id — équivalent
    /// de <c>CCharacter::GetPrestigeExp</c> (@0x08409740) : 0 quand
    /// l'entrée n'existe pas encore dans le miroir.</summary>
    private static int GetPrestigeExp(EntryState st, short prestigeId)
        => st.Prestige.TryGetValue(prestigeId, out var e) ? e.Exp : 0;

    /// <summary>Niveau prestige courant — équivalent de
    /// <c>CCharacter::GetPrestigeLevel</c> (@0x084096d0) : 0 si
    /// l'entrée n'existe pas.</summary>
    private static short GetPrestigeLevel(EntryState st, short prestigeId)
        => st.Prestige.TryGetValue(prestigeId, out var e) ? e.Level
            : (short)0;

    /// <summary>Crédit d'exp prestige — monte au monde via ZW 636
    /// <c>gain_prestige &lt;ch&gt; &lt;id&gt; &lt;delta&gt;</c>
    /// (équivalent du texte DBA natif <c>"GainExp Prestige %hd = %d"</c>
    /// de <c>CPlayerFactory::AddPrestigeExp</c> @0x080f3120). Le monde
    /// persiste + émet 60 au client et renvoie
    /// <c>update_prestige</c> — le miroir local se met à jour au retour.</summary>
    private async Task GainPrestigeExpAsync(EntryState st,
        short prestigeId, int delta, CancellationToken token)
    {
        if (delta == 0 || !_world.Connected) return;
        await _world.SendTextCommandAsync(
            $"gain_prestige {st.CharId} {prestigeId} {delta}", token);
    }

    /// <summary>Set de niveau prestige — ZW 636
    /// <c>set_prestige &lt;ch&gt; &lt;id&gt; &lt;level&gt;</c>
    /// (équivalent du texte natif <c>"SetLevel Prestige %hd = %hd"</c>
    /// — <c>TC_SetPrestigeLevel</c> zone @0x083cd160 → mission).</summary>
    private async Task SetPrestigeLevelAsync(EntryState st,
        short prestigeId, short level, CancellationToken token)
    {
        if (!_world.Connected) return;
        await _world.SendTextCommandAsync(
            $"set_prestige {st.CharId} {prestigeId} {level}", token);
    }
}
