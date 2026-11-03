namespace EdenClient.Protocol;

/// <summary>
/// État partagé des titres (appellations) — rempli par les NCIDs
/// 396/397/398 et consommé par <c>ProfileWindow</c> (sélecteur) et la
/// bulle de notification.
///
/// Sémantique serveur (ZoneEntryFlow.Titles.cs — portage natif) :
///   - 397 <see cref="ZoneUpdateAppellation"/> = titre actif du self
///     (réponse au 93 accepté) ;
///   - 398 <see cref="ZoneAddAppellation"/> = titre nouvellement
///     possédé (grant) ;
///   - 396 <see cref="ZoneChangeAppellation"/> = titre actif d'un
///     personnage visible (broadcast voyants).
///
/// Limite connue : la liste complète des titres possédés n'est émise
/// ni par le serveur C# ni par le natif (vecteur char+0x6d8 chargé
/// depuis la DB sans NotifyClient) — <see cref="Owned"/> ne couvre
/// que les titres appris en session (398) plus le titre actif.
///
/// Mutations sur le thread principal uniquement — lecture sans lock.
/// </summary>
public static class PlayerTitles
{
    /// <summary>Émis à chaque mutation (397/398/396 pour le self).</summary>
    public static event Action? Changed;

    /// <summary>Titre actif du joueur (397 — 0 = aucun).</summary>
    public static int Active { get; private set; }

    /// <summary>Titres possédés connus de la session (398 + actif).</summary>
    public static IReadOnlyCollection<int> Owned => _owned;
    private static readonly SortedSet<int> _owned = new();

    /// <summary>Titre actif par charId visible (396 — pour les
    /// nameplates/infobulles futurs ; 0 = retiré).</summary>
    public static IReadOnlyDictionary<int, (int TitleId, byte Seq)>
        Others => _others;
    private static readonly Dictionary<int, (int, byte)> _others = new();

    /// <summary>Émetteur du 93 — branché par le binder réseau
    /// (AchievementWindow) ; <see cref="RequestSet"/> ne fait rien
    /// hors-ligne.</summary>
    public static Func<int, Task>? SetTitleSender { get; set; }

    /// <summary>Log optionnel des erreurs d'envoi (GD.PrintErr côté
    /// jeu) — ce fichier reste compilé par NetTests sans GodotSharp.</summary>
    public static Action<string>? Log;

    /// <summary>Demande de changement de titre (UI) → 93 via
    /// <see cref="SetTitleSender"/>. 0 = retirer.</summary>
    public static void RequestSet(int titleId)
    {
        var sender = SetTitleSender;
        if (sender == null) return;
        _ = sender(titleId).ContinueWith(t =>
            Log?.Invoke($"[titles] send 93 : {t.Exception?.Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>397 — titre actif du joueur.</summary>
    public static void ApplyUpdate(int titleId)
    {
        Active = titleId;
        if (titleId > 0) _owned.Add(titleId);
        Changed?.Invoke();
    }

    /// <summary>398 — titre nouvellement possédé.</summary>
    public static void ApplyAdd(int titleId)
    {
        if (titleId <= 0) return;
        _owned.Add(titleId);
        Changed?.Invoke();
    }

    /// <summary>396 — titre d'un personnage visible. Si le charId est
    /// le self (cas du broadcast incluant l'émetteur), l'actif est
    /// aussi rafraîchi.</summary>
    public static void ApplyChange(int charId, int titleId, byte seq,
        int selfCharId)
    {
        _others[charId] = (titleId, seq);
        if (charId == selfCharId)
        {
            Active = titleId;
            if (titleId > 0) _owned.Add(titleId);
            Changed?.Invoke();
        }
    }

    /// <summary>Reset session (déconnexion / sortie de zone).</summary>
    public static void Clear()
    {
        Active = 0;
        _owned.Clear();
        _others.Clear();
        Changed?.Invoke();
    }
}
