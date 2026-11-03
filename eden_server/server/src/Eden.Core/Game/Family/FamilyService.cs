using System.Collections.Concurrent;
using Eden.Core.Logging;

namespace Eden.Core.Game.Family;

/// <summary>
/// État famille en mémoire côté monde — équivalent de
/// <c>CGlobalInfo::AllFamilys</c> (map&lt;int, CFamily*&gt;) +
/// <c>AllCharacters</c> (SCharacter) du binaire natif. Chargé au boot
/// depuis PostgreSQL (<see cref="FamilyRepository.LoadAllAsync"/>),
/// muté par les opérations <c>CNE_CW_ServerFamilyOp</c> (NCID 9).
/// Thread-safe : les handlers monde sont des tâches libres, les
/// mutations passent par <see cref="LockedAsync"/>.
/// </summary>
public sealed class FamilyService
{
    private readonly FamilyRepository _repo;
    private readonly ConcurrentDictionary<int, Family> _families = new();
    /// <summary>charId → familyId (<c>SCharacter.family_id</c>).</summary>
    private readonly ConcurrentDictionary<int, int> _memberFamily = new();
    /// <summary>Invitations en attente : charId cible → invite.</summary>
    private readonly ConcurrentDictionary<int, PendingInvite> _invites = new();
    /// <summary>Verrou global des mutations famille (les handlers CW
    /// s'exécutent en tâches libres — pas de boucle acteur ici).</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Invitation en cours (op 1 → attente op 3/4/2).</summary>
    public sealed record PendingInvite(int FamilyId, int InviterId, string InviterName, string FamilyName);

    /// <summary>Crée le service sur le repository famille.</summary>
    public FamilyService(FamilyRepository repo) => _repo = repo;

    /// <summary>Toutes les familles (vue de <c>AllFamilys</c>).</summary>
    public IReadOnlyDictionary<int, Family> Families => _families;

    /// <summary>Chargement initial — reproduit
    /// <c>CNE_DBA_ClientAllFamily::Execute</c> (le monde reçoit toutes
    /// les familles + membres au boot depuis l'agent DB).</summary>
    public async Task LoadAsync()
    {
        foreach (var f in await _repo.LoadAllAsync())
        {
            _families[f.Id] = f;
            foreach (var m in f.Members.Keys)
                _memberFamily[m] = f.Id;
        }
        EdenLog.Write($"[Family] {_families.Count} familles chargées, {_memberFamily.Count} membres");
    }

    /// <summary>Famille par id (<c>CGlobalInfo::GetFamily</c>).</summary>
    public Family? GetFamily(int familyId) =>
        familyId == 0 ? null : (_families.TryGetValue(familyId, out var f) ? f : null);

    /// <summary>Famille d'un personnage (<c>CPlayerEntity::GetFamilyID</c>).</summary>
    public Family? GetFamilyOf(int charId) =>
        _memberFamily.TryGetValue(charId, out var fid) ? GetFamily(fid) : null;

    /// <summary>Résumé d'un membre de famille.</summary>
    public FamilyMemberSummary? GetMember(Family fam, int charId) =>
        fam.Members.TryGetValue(charId, out var m) ? m : null;

    /// <summary>Marque un membre en ligne / hors ligne
    /// (<c>SCharacter+0x18</c> natif — mis à jour au login/logout monde).</summary>
    public void SetOnline(int charId, bool online)
    {
        if (GetFamilyOf(charId) is { } fam
            && fam.Members.TryGetValue(charId, out var m))
            m.Online = online;
    }

    /// <summary>Privilège natif d'un membre pour l'opération
    /// <paramref name="privilegeIndex"/> : le chef a tous les droits ;
    /// sinon <c>family.*_privilege</c> donne le rang minimum requis
    /// (0 = tous membres).</summary>
    public static bool HasPrivilege(Family fam, int charId, int privilegeIndex)
    {
        if (charId == fam.LeaderId) return true;
        if (!fam.Members.TryGetValue(charId, out var m)) return false;
        if (privilegeIndex < 1 || privilegeIndex > Family.PrivilegeCount) return false;
        return m.Rank >= fam.Privileges[privilegeIndex - 1];
    }

    /// <summary>Exécute <paramref name="action"/> sous le verrou famille.</summary>
    public Task LockedAsync(Func<Task> action) => LockedAsync<Task>(async () => { await action(); return Task.CompletedTask; });

    /// <summary>Exécute <paramref name="action"/> sous le verrou famille
    /// et retourne son résultat.</summary>
    public async Task<T> LockedAsync<T>(Func<Task<T>> action)
    {
        await _gate.WaitAsync();
        try { return await action(); }
        finally { _gate.Release(); }
    }

    /// <summary>Enregistre une invitation (op 1).</summary>
    public void AddInvite(int targetId, PendingInvite invite) => _invites[targetId] = invite;

    /// <summary>Consomme une invitation adressée à
    /// <paramref name="targetId"/> émise par <paramref name="inviterId"/>
    /// (op 3/4 — Field2 = charId de l'inviteur).</summary>
    public bool TryConsumeInvite(int targetId, int inviterId, out PendingInvite invite)
    {
        invite = null!;
        return _invites.TryGetValue(targetId, out var i)
            && i.InviterId == inviterId
            && _invites.TryRemove(targetId, out invite!);
    }

    /// <summary>Retire l'invitation émise par un joueur
    /// (op 2 cancel — une invitation sortante par joueur).</summary>
    public void CancelInvitesOf(int inviterId)
    {
        foreach (var kv in _invites.Where(kv => kv.Value.InviterId == inviterId).ToList())
            _invites.TryRemove(kv.Key, out _);
    }

    /// <summary>Crée une famille (fil natif Zone→World→Mission
    /// <c>CreateFamily</c> : INSERT family + leader devient membre).</summary>
    public async Task<Family?> CreateFamilyAsync(string name, FamilyMemberSummary leader)
    {
        return await LockedAsync(async () =>
        {
            if (GetFamilyOf(leader.CharacterId) is not null) return null;
            if (_families.Values.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
                return null;
            var id = await _repo.CreateFamilyAsync(name, leader.CharacterId,
                (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var fam = new Family { Id = id, Name = name, LeaderId = leader.CharacterId, LeaderName = leader.Name };
            fam.Members[leader.CharacterId] = leader;
            _families[id] = fam;
            _memberFamily[leader.CharacterId] = id;
            await _repo.JoinAsync(leader.CharacterId, id);
            return fam;
        });
    }

    /// <summary>Ajoute un membre (op 3 accept / 0x50 recruit join) —
    /// appelé sous <see cref="LockedAsync"/>.</summary>
    public void AddMember(Family fam, FamilyMemberSummary member)
    {
        fam.Members[member.CharacterId] = member;
        _memberFamily[member.CharacterId] = fam.Id;
    }

    /// <summary>Retire un membre (op 5 quit / 6 kick / 8 disband) —
    /// appelé sous <see cref="LockedAsync"/>.</summary>
    public void RemoveMember(Family fam, int charId)
    {
        fam.Members.Remove(charId);
        _memberFamily.TryRemove(charId, out _);
    }

    /// <summary>Supprime la famille du cache — appelé sous verrou.</summary>
    public void RemoveFamily(Family fam)
    {
        foreach (var id in fam.Members.Keys)
            _memberFamily.TryRemove(id, out _);
        _families.TryRemove(fam.Id, out _);
    }

    /// <summary>Repository sous-jacent pour les handlers.</summary>
    public FamilyRepository Repo => _repo;

    /// <summary>Résultat de <see cref="InstanceCreateAsync"/> —
    /// l'id de texte natif à renvoyer au demandeur (0 = succès) et le
    /// node de hall attribué.</summary>
    public readonly record struct InstanceCreateResult(short TextIndex,
        short Node)
    {
        public bool Success => TextIndex == 0;
    }

    /// <summary>Coût en trésorerie d'un hall — vip 0 → 500 000,
    /// vip 1 → 10 000 000 (<c>CFamily::InstanceCreate</c>).</summary>
    public static int InstanceCost(byte vip) => vip == 0 ? 500_000 : 10_000_000;

    /// <summary>Node de hall pour un type de création — type 1 → 501
    /// (0x1f5), type 0 → 502 (0x1f6) ; autres → -1 (abandon silencieux
    /// natif).</summary>
    public static short InstanceNode(short type) =>
        type switch { 1 => 501, 0 => 502, _ => (short)-1 };

    /// <summary>Gates natifs de <c>CFamily::InstanceCreate</c>
    /// (mission @0x81bf660) dans l'ordre du désassemblage — pur,
    /// testable : house_open≠0 →0x91a (gate zone relayée), niveau≤29
    /// →0x599 (exec+0x5c &lt;= 0x1d), demandeur≠chef →0x598,
    /// lv&lt;3 →0x59a, vip 0 &amp;&amp; villes&gt;49 →0x59c,
    /// trésorerie →0x6ae, &lt;30 membres (fam+0x108) →0x59b,
    /// famille recyclée (<c>AllBeRecycledFamilyIDs</c>) →0xa4b.
    /// Retour : index de texte (0 = passe, -1 = abandon silencieux
    /// pour type hors {0,1}).</summary>
    public static short InstanceCreateGate(Family fam, int charId,
        short type, byte vip, int charLevel, int openTowns)
    {
        if (fam.HouseOpen != 0) return 0x91a;
        if (charLevel <= 29) return 0x599;
        if (fam.LeaderId != charId) return 0x598;
        if (fam.Level < 3) return 0x59a;
        if (vip == 0 && openTowns > 49) return 0x59c;
        if (fam.Treasury < InstanceCost(vip)) return 0x6ae;
        if (fam.Members.Count < 30) return 0x59b;
        if (fam.RecycledTime != 0) return 0xa4b;
        return InstanceNode(type) < 0 ? (short)-1 : (short)0;
    }

    /// <summary>Création du hall de famille — mutation de
    /// <c>CFamily::InstanceCreate</c> : persiste
    /// <c>family.house_open</c>, débite la trésorerie, journalise
    /// (UpdateLog type 2, str1 = nom) — sous le verrou famille. Les
    /// gates sont <see cref="InstanceCreateGate"/>.</summary>
    /// <param name="charLevel">Niveau du demandeur (exec+0x5c natif).</param>
    public async Task<InstanceCreateResult> InstanceCreateAsync(
        int charId, short type, byte vip, int charLevel)
    {
        return await LockedAsync(async () =>
        {
            var fam = GetFamilyOf(charId);
            if (fam is null) return new InstanceCreateResult(0x598, 0);
            var openTowns = vip == 0
                ? await _repo.CountOpenTownsAsync() : 0;
            var gate = InstanceCreateGate(fam, charId, type, vip,
                charLevel, openTowns);
            if (gate != 0) return new InstanceCreateResult(gate, 0);
            var node = InstanceNode(type);
            var cost = InstanceCost(vip);
            await _repo.SetHouseOpenAsync(fam.Id, node);
            fam.Treasury = await _repo.AddTreasuryAsync(fam.Id, -cost);
            fam.HouseOpen = node;
            // UpdateLog natif : type 2, str1 = nom de famille
            // (local_1c = this+0xc dans InstanceCreate).
            await _repo.AddLogAsync(fam.Id, 2, 0, fam.Name, "");
            return new InstanceCreateResult(0, node);
        });
    }
}
