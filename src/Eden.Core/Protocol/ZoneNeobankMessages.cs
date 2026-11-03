using EdenClient.Net;

namespace EdenClient.Protocol;

// ===================================================================
// « Neobank » — PAS de protocole propre.
//
// AUDIT re/ghidra/rf_neobank (NeoInventoryBank.dll) : la DLL est une
// extension **purement côté client** greffée par hooks sur les fenêtres
// natives `StorageUI` (banque) et `Root/Bag` (inventaire). Aucune
// chaîne réseau, aucun NCID, aucun format de paquet dans
// strings.txt/decompiled.c — les seuls exports réseau-adjacent sont
// l'infrastructure de hooks (trampoline/queue/commit).
//
// Ce que la DLL ajoute (preuves = strings.txt + layout XML embarqué) :
//   - features ini `neoBank_StorageUI` / `neoBank_Inventory` :
//     `Columns` (16..32, défaut 16), `Rows` (1..20), `ScrollMode`
//     (0 = scroll natif, 1 = fenêtré paginé) ;
//   - grille redimensionnable de slots `Root/StorageUI/backgrand/
//     border/bag%u` remplie par les items du conteneur courant ;
//   - barre `neo/Bank/Footer` : boutons `Columns`/`Rows`/`Mode`,
//     navigation `Previous`/`Page "x/y"`/`Next` ;
//   - recherche `neo/Bank/Query` (editbox, tooltip « Search by item
//     name or ID. Press Escape to clear. ») + `HoverId` (« Item ID »)
//     + hint `SearchHint` (« Search by name or item ID... ») ;
//   - menu contextuel `neo/Bank/Popup` + `Choice0`..`Choice19` ;
//   - verrous clients `Fneo/LockedPool/%u/%u/%u` : cadre doré
//     FFFFFF60 sur fond sombre A0000000/28FFD040 par slot.
//
// Données véhiculées : RIEN de nouveau — la banque reste le protocole
// natif déjà implémenté (357 EnterStorage → CZ 62 GetStorageData →
// 358 contenu ; dépôt CZ 60, retrait CZ 61, cf. ZoneShopMessages.cs +
// BankWindow.cs). **Aucun handler serveur n'est requis.**
//
// Ce fichier porte donc uniquement : les paramètres persistés
// (user://neo_bank.cfg) et l'état des verrous côté client.
// ===================================================================

/// <summary>
/// Paramètres neobank (miroir des clés ini de la DLL : Columns 16..32,
/// Rows 1..20, ScrollMode 0/1) persistés dans
/// <c>user://neo_bank.cfg</c> — même convention ConfigFile que
/// <c>DragHelper</c> (<c>ui_layout.cfg</c>). Client-side only.
/// </summary>
public static class NeobankSettings
{
    private const string Path = "user://neo_bank.cfg";
    private const string Section = "bank";

    /// <summary>Colonnes de la grille banque (DLL : clamp 16..32, défaut 16).</summary>
    public static int Columns { get; set; } = 16;
    /// <summary>Lignes de la grille banque (DLL : clamp 1..20, défaut 8).</summary>
    public static int Rows { get; set; } = 8;
    /// <summary>0 = défilement natif (scroll), 1 = pages Previous/Next.</summary>
    public static int ScrollMode { get; set; } = 1;
    /// <summary>Neobank remplace la banque classique à l'ouverture
    /// (clé « Enabled » des features ini de la DLL — StorageUI et
    /// Inventory sont deux features séparées chez elle ; ici la
    /// bascule porte sur la vue banque).</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Slots par page courante.</summary>
    public static int SlotsPerPage => Columns * Rows;

    /// <summary>Colonnes +4 cyclées dans 16..32 (bouton « N cols »).</summary>
    public static int CycleColumns() =>
        Columns = Columns >= 32 ? 16 : Columns + 4;

    /// <summary>Lignes +2 cyclées dans 1..20 (bouton « N rows »).</summary>
    public static int CycleRows() =>
        Rows = Rows >= 20 ? 1 : Rows + 2;

    /// <summary>Bascule scroll ↔ pages (bouton « Use Scroll/Pages »).</summary>
    public static int ToggleMode() => ScrollMode = 1 - ScrollMode;

    /// <summary>Charge les paramètres persistés (défauts si absent).</summary>
    public static void Load()
    {
    }

    /// <summary>Persiste les paramètres (best effort).</summary>
    public static void Save()
    {
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
}

/// <summary>
/// Verrous d'items côté client — miroir du « LockedPool » de la DLL
/// (cadre doré <c>FFFFFF60</c> par slot verrouillé, les items verrouillés
/// ne partent pas au dépôt/retrait d'un clic). Aucun effet serveur :
/// pure UX, persistée par conteneur+slot dans <c>user://neo_bank.cfg</c>
/// section « locks ». <b>Caveat</b> : comme la DLL, la clé est le
/// couple (conteneur, slot) — un slot ré-utilisé reste marqué.
/// </summary>
public static class NeobankLocks
{
    private const string Path = "user://neo_bank.cfg";
    private const string Section = "locks";

    /// <summary>Émis quand l'ensemble des verrous change.</summary>
    public static event Action? Changed;

    private static readonly HashSet<long> _locked = new();

    private static long KeyOf(int container, int slot) =>
        ((long)container << 32) | (uint)slot;

    /// <summary>Vrai si le slot banque est verrouillé.</summary>
    public static bool IsLocked(int container, int slot) =>
        _locked.Contains(KeyOf(container, slot));

    /// <summary>Bascule le verrou d'un slot (persiste).</summary>
    public static bool Toggle(int container, int slot)
    {
        var key = KeyOf(container, slot);
        var on = _locked.Add(key);
        if (!on) _locked.Remove(key);
        Save();
        Changed?.Invoke();
        return on;
    }

    /// <summary>Retire les verrous des slots au-delà d'une capacité
    /// (conteneur réduit — évite les verrous orphelins).</summary>
    public static void Prune(int container, int capacity)
    {
        var dirty = _locked.RemoveWhere(k =>
            (int)(k >> 32) == container && (int)k >= capacity) > 0;
        if (dirty)
        {
            Save();
            Changed?.Invoke();
        }
    }

    /// <summary>Recharge les verrous persistés.</summary>
    public static void Load()
    {
    }

    private static void Save()
    {
    }

    /// <summary>Efface les clés lock obsolètes avant réécriture —
    /// appelé dans Save via réflexion nulle : Godot expose
    /// <c>EraseSectionKey</c>.</summary>
    public static void Wipe()
    {
        _locked.Clear();
        Changed?.Invoke();
    }
}
