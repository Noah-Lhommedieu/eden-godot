using Eden.Core.Game.Family;
using Eden.Core.Logging;

namespace Eden.World;

/// <summary>
/// Maintenance des villes de guilde — <c>CEventFamilyInstanceRecycleCheck::
/// Execute</c> mission @0x081e4040 : un événement par famille ayant une
/// ville (créé au chargement / à <c>InstanceCreate</c>), 1re échéance à
/// +60 s puis toutes les heures (0xe10 s). À chaque échéance :
/// famille absente ou ≥ 10 membres en ligne (<c>+0x84</c> = ensemble des
/// membres connectés, incrémenté à <c>TC_LoginFamily</c>) → l'événement
/// s'arrête ; sinon compteur +1, et à 72 (0x48) →
/// <c>CFamily::ClearInstanceData</c> @0x081b53e0 : <c>house_open</c> = 0
/// (<c>UpdateInstanceNodeID(fam, 0)</c>), <c>UpdateRecycleTime</c>,
/// diffusion <c>ClientChangeFamily</c> (op 0x1a), retrait des villes
/// ouvertes. Les bâtiments/PNJ ne sont pas supprimés.
/// Réglages de test : <c>EDEN_TOWN_RECYCLE_TICK_SEC</c> (3600),
/// <c>EDEN_TOWN_RECYCLE_FIRST_SEC</c> (60), <c>EDEN_TOWN_RECYCLE_TICKS</c>
/// (72), <c>EDEN_TOWN_RECYCLE_ONLINE</c> (10).
/// </summary>
public static class TownRecycleService
{
    private sealed class State { public int Ticks; public bool Active = true; }
    private static readonly Dictionary<int, State> _states = new();

    private static int Env(string name, int def) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0
            ? v : def;

    /// <summary>Règle pure d'une échéance : retourne l'action.</summary>
    public enum Step { Stop, Tick, Recycle }

    public static Step Evaluate(bool familyExists, int onlineMembers,
        ref int ticks, int onlineThreshold = 10, int maxTicks = 72)
    {
        if (!familyExists || onlineMembers >= onlineThreshold) return Step.Stop;
        ticks++;
        return ticks >= maxTicks ? Step.Recycle : Step.Tick;
    }

    public static async Task RunAsync(FamilyService families,
        Func<Family, Task> onRecycled, CancellationToken token)
    {
        var first = Env("EDEN_TOWN_RECYCLE_FIRST_SEC", 60);
        var tick = Env("EDEN_TOWN_RECYCLE_TICK_SEC", 3600);
        var maxTicks = Env("EDEN_TOWN_RECYCLE_TICKS", 72);
        var threshold = Env("EDEN_TOWN_RECYCLE_ONLINE", 10);
        try { await Task.Delay(TimeSpan.FromSeconds(first), token); }
        catch (OperationCanceledException) { return; }
        while (!token.IsCancellationRequested)
        {
            foreach (var fam in families.Families.Values.ToList())
            {
                if (fam.HouseOpen == 0) { _states.Remove(fam.Id); continue; }
                var st = _states.TryGetValue(fam.Id, out var s0) ? s0
                    : _states[fam.Id] = new State();
                if (!st.Active) continue;
                var ticks = st.Ticks;
                var step = Evaluate(true, fam.Members.Values.Count(m => m.Online), ref ticks,
                    threshold, maxTicks);
                st.Ticks = ticks;
                if (step == Step.Stop)
                {
                    st.Active = false;
                    EdenLog.Write($"[TownRecycle] fam={fam.Id} ≥{threshold} " +
                        "en ligne : contrôle arrêté");
                    continue;
                }
                EdenLog.Write($"[TownRecycle] fam={fam.Id} en ligne=" +
                    $"{fam.Members.Values.Count(m => m.Online)} tick {ticks}/{maxTicks}");
                if (step != Step.Recycle) continue;
                try
                {
                    await families.LockedAsync(async () =>
                    {
                        fam.HouseOpen = 0;
                        fam.RecycledTime = (uint)DateTimeOffset.UtcNow
                            .ToUnixTimeSeconds();
                        await families.Repo.SetHouseOpenAsync(fam.Id, 0);
                        await families.Repo.SetRecycledTimeAsync(fam.Id,
                            (int)fam.RecycledTime);
                    });
                    _states.Remove(fam.Id);
                    EdenLog.Write($"[TownRecycle] fam={fam.Id} ville recyclée " +
                        "(ClearInstanceData)");
                    await onRecycled(fam);
                }
                catch (Exception ex)
                {
                    EdenLog.Write($"[TownRecycle] fam={fam.Id} erreur : {ex.Message}");
                }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(tick), token); }
            catch (OperationCanceledException) { return; }
        }
    }
}
