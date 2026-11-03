namespace Eden.Core.Game.Inventory;

/// <summary>
/// Table des emplacements de tenue rapide (FastDress) — transcription
/// de `GameData::FastDressEquipLoc` (zone .rodata @0x087c36e0, 24 u16)
/// et des traductions de `CPlayerFactory::GetFastDressEquipLocTranslate`
/// / `GetEquipmentEquipLocTranslate` / `GetTypeFromSlotLoc`
/// (zone @0x080ed0c0/0x080ed1b0/0x080ed210).
///
/// Une « clé dress » (`tar_loc`) identifie un emplacement de set :
/// les clés directes (7,8,14,15,11,12,13,6,10) couvrent armes et
/// accessoires ; les clés 601-605/611-615/621-625 couvrent les
/// armures (kind 1-5) par colonne d'archetype de classe
/// (`CClassData+0x28` — 1→+620, 2→+610, 3→+600).
/// </summary>
public static class FastDressSlots
{
    /// <summary>Les 24 clés natives, dans l'ordre du bitmask client —
    /// bit i du mask de `ApplyFastDressData` = `EquipLocs[i]`
    /// (char+0x5bc peuplé à l'init @0x8435xxx : key→index).</summary>
    public static readonly ushort[] EquipLocs =
    [
        7, 8, 14, 15, 11, 12, 13, 6, 10,
        601, 602, 603, 605, 604,
        611, 612, 613, 615, 614,
        621, 622, 623, 625, 624,
    ];

    /// <summary>Nombre de pages natives (LoadFastDress itère 0..2 —
    /// zone @0x82efbxx).</summary>
    public const int PageCount = 3;

    /// <summary>Bitpos d'une clé dans le mask client (index dans
    /// <see cref="EquipLocs"/>), -1 si clé inconnue.</summary>
    public static int BitPos(ushort key)
    {
        var i = Array.IndexOf(EquipLocs, key);
        return i < 0 ? -1 : i;
    }

    /// <summary>Vrai si le bit de la clé est posé dans le mask
    /// (`ConfirmApplyByCheckBoxStatus` @0x80ed250).</summary>
    public static bool Checked(int mask, ushort key)
    {
        var i = BitPos(key);
        return i >= 0 && ((mask >> i) & 1) != 0;
    }

    /// <summary>Colonne d'armure d'une clé :
    /// `GetTypeFromSlotLoc` — 601-605→2, 611-615→3, 621-625→4,
    /// autres→-1 (0x80ed210). Retourne le « type » natif.</summary>
    public static int ArmorColumn(ushort key) => key switch
    {
        >= 601 and <= 605 => 2,
        >= 611 and <= 615 => 3,
        >= 621 and <= 625 => 4,
        _ => -1,
    };

    /// <summary>Clé armure → kind d'armure (601→1…625→5) ;
    /// retourne -1 pour une clé non-armure.</summary>
    public static int ArmorKind(ushort key)
    {
        var col = ArmorColumn(key);
        return col < 0 ? -1 : key - (col * 10 + 580);
        // col2→-600, col3→-610, col4→-620 : key-(600+10*(col-2))
    }

    /// <summary>Traduction kind d'armure (1-5) → clé dress selon
    /// l'archetype de classe (`CClassData+0x28` ∈{1,2,3}) —
    /// `GetFastDressEquipLocTranslate` @0x80ed0c0 :
    /// arch3→+600, arch2→+610, arch1→+620. Hors plage → kind brut.</summary>
    public static ushort ArmorKey(int kind, int archetype)
    {
        if (kind < 1 || kind > 5) return (ushort)kind;
        return (ushort)(kind + archetype switch
        {
            3 => 600,
            2 => 610,
            _ => 620,
        });
    }

    /// <summary>Groupe d'armure natif dérivé du kind —
    /// <c>CheckFastDressUseItem</c> @0x080ff560 : masques
    /// 0x2492={1,4,7,10,13}→4, 0x4924={2,5,8,11,14}→3,
    /// 0x9248={3,6,9,12,15}→2 ; kinds 16-39 → 0 ; autre → -1
    /// (non dressable → texte 0x59e).</summary>
    public static int GroupOfKind(int kind)
    {
        if (kind < 1 || kind > 39) return -1;
        if (kind > 15) return 0;
        // NB : les parenthèses sont requises — `kind % 3 switch` se
        // parserait `kind % (3 switch …)` (le switch lie plus fort
        // que %), ce qui évaluait le switch sur la constante 3.
        return (kind % 3) switch
        {
            0 => 2,   // {3,6,9,12,15} → colonne 2 (clés 601+)
            2 => 3,   // {2,5,8,11,14} → colonne 3 (clés 611+)
            _ => 4,   // {1,4,7,10,13} → colonne 4 (clés 621+)
        };
    }

    /// <summary>Colonne d'armure d'une clé dress — 601-605→2,
    /// 611-615→3, 621-625→4, clés directes → 0.</summary>
    public static int ColumnOfKey(int key) => key switch
    {
        >= 601 and <= 605 => 2,
        >= 611 and <= 615 => 3,
        >= 621 and <= 625 => 4,
        _ => 0,
    };

    /// <summary>Position d'équipement cible d'une clé — (groupe
    /// <c>container_index</c> natif, loc) : clés directes → (0, clé) ;
    /// clés armure → (colonne, clé-base).</summary>
    public static (int group, int loc) EquipPosOf(int key) => key switch
    {
        >= 601 and <= 605 => (2, key - 600),
        >= 611 and <= 615 => (3, key - 610),
        >= 621 and <= 625 => (4, key - 620),
        _ => (0, key),
    };

    /// <summary>Alias int de <see cref="Checked"/> pour les clés
    /// passées en int (wire i32).</summary>
    public static bool BitIsSet(int mask, int key)
        => key is >= 0 and <= ushort.MaxValue &&
           Checked(mask, (ushort)key);

    /// <summary>Clés dress produites par un item équipé/déséquipé —
    /// `UpdateFastDressFromInfo` @0x808b7xx : les kinds à double
    /// emplacement écrivent DEUX clés, les armures une clé par
    /// colonne de leur `EquipLoc` (groupes natifs 0x9248/0x4924/0x2492
    /// = loc mod 3). Kinds non dressés (filtre natif : hors
    /// {1-12,0x13,0x14,0x22,0x23}) → vide.</summary>
    public static int[] KeysForItemKind(int kind, int equipLoc)
    {
        // Filtre natif : `if ((0xb < kind-1) && (1 < kind-0x13) &&
        // kind != 0x22 && kind != 0x23) return` — les kinds
        // 0x16+ hors 0x22/0x23 ne touchent aucune clé ; 0x0d-0x15
        // tombent dans la branche « no key » de la fonction.
        switch (kind)
        {
            case 7 or 0x13: return [7, 8];       // 1H/dague : paire mains
            case 0x0c: return [14, 15];          // paire accessoire
            case 9: return [7];                  // 2H : main seule
            case 0x23: return [10];
            case 10 or 0x22: return [11];
            case 0x0b or 0x14: return [12, 13];
            case >= 1 and <= 6:
            case 8:
            {
                // kind+colonne dérivée de l'EquipLoc (loc%3 :
                // {3,6,9,12,15}→+600, {2,5,8,11,14}→+610,
                // {1,4,7,10,13}→+620 ; loc autre → kind brut).
                var off = (equipLoc % 3) switch
                {
                    0 => 600,
                    2 => 610,
                    _ => 620,
                };
                return equipLoc is >= 1 and <= 15
                    ? [kind + off] : [kind];
            }
            default: return [];
        }
    }
}
