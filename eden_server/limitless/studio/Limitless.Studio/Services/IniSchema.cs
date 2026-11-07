using System.Collections.Generic;

namespace Limitless.Studio.Services;

/// <summary>Type de champ d'une colonne .ini — pilote la validation
/// du formulaire (Number = décimal uniquement, Script = texte libre
/// `#:Cmd …`, Name = libellé Big5 dont la version EN vit dans le
/// T_*.ini client, Code = clé d'asset, Flags = bitmask numérique).</summary>
public enum IniFieldKind
{
    Text,
    Number,
    Code,
    Name,
    Flags,
    Script,
}

/// <summary>Une colonne documentée d'une table .ini — index 0-basé,
/// libellé EN, explication d'utilité (affichée sous le champ),
/// groupe de section du formulaire, et `RefTrans` = fichier T_*.ini
/// dont le nom EN résout la valeur quand c'est un id référencé.</summary>
public sealed record IniColumn(
    int Index,
    string Name,
    string Hint,
    IniFieldKind Kind = IniFieldKind.Text,
    string Group = "Other",
    string? RefTrans = null);

/// <summary>Schéma d'une table .ini de `db_decoded` — construit à
/// partir des parsers C# du serveur (`server/src/Eden.Core/Data/
/// Parsers/*Parser.cs`, commentaires de colonnes désasm).</summary>
/// <param name="File">Nom de fichier, ex. "S_Item.ini".</param>
/// <param name="Title">Titre lisible, ex. "Items".</param>
/// <param name="Purpose">À quoi sert la table, une phrase.</param>
/// <param name="Impact">Ce que modifier ce fichier implique
/// (autres fichiers à vérifier, redémarrage, copie client).</param>
/// <param name="TransFile">T_*.ini client portant les noms EN
/// (null = pas de traduction connue).</param>
/// <param name="NameColumn">Index de colonne contenant le nom
/// d'affichage (-1 = aucun).</param>
/// <param name="Columns">Colonnes documentées ; toute colonne absente
/// du schéma est affichée comme « col N — undocumented ».</param>
public sealed record IniTableSchema(
    string File,
    string Title,
    string Purpose,
    string Impact,
    string? TransFile,
    int NameColumn,
    IReadOnlyList<IniColumn> Columns);
