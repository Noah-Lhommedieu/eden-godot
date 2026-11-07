using System.Collections.Generic;

namespace Limitless.Studio.Services;

/// <summary>Une valeur possible d'un champ .ini — le code brut
/// tel qu'écrit dans la colonne (ex. "44", "0x80", "2") et son sens
/// complet (ex. "mount / generic usable — pair with use type 2 + a
/// Ride enchant").</summary>
public sealed record FieldValueCase(string Value, string Meaning);

/// <summary>Documentation complète d'un champ de formulaire .ini —
/// le contenu affiché par la fenêtre d'aide « ? ». Sections :
/// <list type="bullet">
///   <item><see cref="Purpose"/> — à quoi sert la colonne, formules
///         natives et pièges ;</item>
///   <item><see cref="Values"/> — table de référence : TOUS les cas
///         connus (enum/flags → chaque valeur nommée) ; les valeurs
///         réellement présentes en data mais non listées ici sont
///         ajoutées automatiquement par la fenêtre depuis
///         <see cref="IniColumnStats"/> ;</item>
///   <item><see cref="Recipe"/> — « comment obtenir tel cas d'usage »
///         (ex. monture = kind 44 + use 2 + enchant 2134) ;</item>
///   <item><see cref="SeeAlso"/> — fichiers/colonnes liés.</item>
/// </list>
/// Sources : parsers C# serveur (`Eden.Core/Data/Parsers`), désasm
/// cité dans leurs commentaires, et valeurs observées dans
/// `server_us/Data/db_decoded`.</summary>
public sealed record FieldHelpEntry(
    string Purpose,
    IReadOnlyList<FieldValueCase>? Values = null,
    string? ValuesNote = null,
    string? Recipe = null,
    IReadOnlyList<string>? SeeAlso = null)
{
    /// <summary>Entrée générique — utilisée quand la colonne n'a
    /// pas de doc dédiée : le panneau n'affiche alors que la
    /// section « valeurs observées ».</summary>
    public static FieldHelpEntry Generic(string hint) => new(
        hint.Length > 0 ? hint
            : "This column is not mapped in the server parser — its " +
              "meaning is inferred from the values actually shipped " +
              "in the file (see below).");
}
