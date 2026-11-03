using System.Runtime.CompilerServices;
using Dapper;

namespace Eden.Core.Database;

/// <summary>
/// Initialisation globale Dapper — active
/// <see cref="DefaultTypeMap.MatchNamesWithUnderscores"/> pour mapper
/// les colonnes snake_case du schéma natif edenfr_db (item_id, player_id,
/// container_index, cur_maxdurability, create_time…) vers les
/// propriétés PascalCase des rows C#. Sans ce flag, Dapper ignore
/// silencieusement ces colonnes : les entités chargées ont des champs
/// à 0 — ex. bug live : le paquet 312 EnterNodeInventory émettait des
/// items avec ItemId=0 (inventaire affiché vide côté client).
/// </summary>
internal static class DapperBootstrap
{
    /// <summary>Exécuté automatiquement au chargement de l'assembly
    /// Eden.Core — avant toute requête Dapper. Enregistre aussi le
    /// provider CodePages : Big5 (chaînes réseau, .ini db_decoded) et
    /// GBK (dialogues/quêtes natives) ne sont pas des encodages natifs
    /// .NET — sans <see cref="CodePagesEncodingProvider"/>,
    /// <c>GetEncoding("GBK")</c> lève. Centralisé ici : garanti pour
    /// tout processus chargeant Eden.Core, quel que soit l'ordre
    /// d'initialisation des types.</summary>
#pragma warning disable CA2255 // initialisation de bibliothèque voulue
    [ModuleInitializer]
    internal static void Init()
    {
        System.Text.Encoding.RegisterProvider(
            System.Text.CodePagesEncodingProvider.Instance);
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }
}
