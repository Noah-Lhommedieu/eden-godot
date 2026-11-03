using Eden.Core;
using Eden.Core.Database.Entities;
using Eden.Core.Database.Repositories;

// -----------------------------------------------------------------------------
//  Eden.Admin — CLI d'administration (création comptes / personnages)
//
//  Existe parce que le client Reforged a retiré l'interface de création de
//  personnage : tout passe par les mêmes repositories que le flux natif
//  (Eden.World NCIDs 4/5), donc les INSERT sont identiques à ceux produits
//  par la création in-game — player_characters + player_classlist +
//  player_spells + starter items + character_number, en une transaction.
//
//  Connexions : mêmes env vars que les services — DB_CONN (edenfr_db) et
//  ACCOUNT_DB_CONN (edenfr_account), avec les mêmes fallbacks que World.
//
//  Commandes :
//    account create --user <u> --pass <p> [--realname <n>]
//    account list
//    char create --account <user|id> --name <n> --race <1-5> --class <c>
//                [--gender <0|1>] [--hair <h>] [--face <f>]
//                [--hair-color <c>] [--skin-color <c>]
//                [--eyes <c>] [--eyes2 <c>] [--flag <f>]
//    char list --account <user|id>
//    char name-check --name <n>
// -----------------------------------------------------------------------------

await using var db = Eden.Core.Database.DbConnectionFactory.Create(Eden.Core.Database.DbRole.Game);
await using var accountDb = Eden.Core.Database.DbConnectionFactory.Create(Eden.Core.Database.DbRole.Account);
var characters = new CharacterRepository(db);
var accounts = new AccountRepository(accountDb);

if (args.Length == 0) { Usage(); return 1; }

try
{
    return args[0] switch
    {
        "account" => await AccountCmd(args[1..]),
        "char" => await CharCmd(args[1..]),
        _ => Unknown(args[0]),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"erreur: {ex.Message}");
    return 1;
}

// ---------------------------------------------------------------
// account create / list
// ---------------------------------------------------------------
async Task<int> AccountCmd(string[] sub)
{
    if (sub.Length == 0) { Usage(); return 1; }
    switch (sub[0])
    {
        case "create":
        {
            var user = Opt(sub, "--user") ?? Missing("--user");
            var pass = Opt(sub, "--pass") ?? Missing("--pass");
            var realname = Opt(sub, "--realname") ?? "";
            var existing = await accounts.GetAccountByUsernameAsync(user);
            if (existing is not null)
            {
                Console.Error.WriteLine($"erreur: le compte '{user}' existe déjà (id={existing.Id})");
                return 1;
            }
            var id = await accounts.CreateAccountAsync(user, pass, realname);
            Console.WriteLine($"compte créé : '{user}' id={id}");
            return 0;
        }
        case "list":
        {
            await using var conn = accountDb.CreateConnection();
            var rows = await Dapper.SqlMapper.QueryAsync<(int id, string username, int state)>(
                conn, "SELECT id, username, state FROM accounts ORDER BY id;");
            foreach (var r in rows)
                Console.WriteLine($"{r.id,10}  {r.username,-20} state={r.state}");
            return 0;
        }
        default:
            return Unknown($"account {sub[0]}");
    }
}

// ---------------------------------------------------------------
// char create / list / name-check
// ---------------------------------------------------------------
async Task<int> CharCmd(string[] sub)
{
    if (sub.Length == 0) { Usage(); return 1; }
    switch (sub[0])
    {
        case "name-check":
        {
            var name = Opt(sub, "--name") ?? Missing("--name");
            var code = await NameCheckCode(name);
            Console.WriteLine($"name-check '{name}' → code={code} " +
                "(0=ok, 3=pris, 8=trop long, 9=trop court, 10=illégal)");
            return code == 0 ? 0 : 1;
        }
        case "list":
        {
            var (accountId, _) = await ResolveAccount(Opt(sub, "--account") ?? Missing("--account"));
            await using var conn = db.CreateConnection();
            var rows = await Dapper.SqlMapper.QueryAsync<(
                int id, string given_name, int race_id, int class_id, int level,
                int node_id, int deleted_time)>(
                conn, @"SELECT id, given_name, race_id, class_id, level, node_id, deleted_time
                        FROM player_characters WHERE account_id = @accountId ORDER BY id;",
                new { accountId });
            var any = false;
            foreach (var c in rows)
            {
                any = true;
                Console.WriteLine($"{c.id,11}  {c.given_name,-16} race={c.race_id} class={c.class_id} " +
                    $"lv={c.level} node={c.node_id} deleted={(c.deleted_time > 0 ? "oui" : "non")}");
            }
            if (!any) Console.WriteLine("(aucun personnage)");
            return 0;
        }
        case "create":
        {
            var (accountId, accountName) =
                await ResolveAccount(Opt(sub, "--account") ?? Missing("--account"));
            var name = Opt(sub, "--name") ?? Missing("--name");
            var race = IntOpt(sub, "--race") ?? throw new InvalidOperationException("argument requis : --race");
            var cls = IntOpt(sub, "--class") ?? throw new InvalidOperationException("argument requis : --class");
            var gender = IntOpt(sub, "--gender") ?? 0;
            var hair = IntOpt(sub, "--hair") ?? 0;
            var face = IntOpt(sub, "--face") ?? 0;
            var hairColor = IntOpt(sub, "--hair-color") ?? 0;
            var skinColor = IntOpt(sub, "--skin-color") ?? 1;
            var eyes = IntOpt(sub, "--eyes") ?? 0;
            var eyes2 = IntOpt(sub, "--eyes2") ?? 0;
            var flag = (byte)(IntOpt(sub, "--flag") ?? 0);

            var code = await NameCheckCode(name);
            if (code != 0)
            {
                Console.Error.WriteLine($"erreur: nom '{name}' refusé (code={code})");
                return 1;
            }
            if (await characters.GetCharacterCountAsync(accountId) >= 3)
            {
                Console.Error.WriteLine($"erreur: le compte '{accountName}' a déjà 3 personnages (limite native)");
                return 1;
            }

            // Blob 32B : ré-encodage inverse des bitfields natifs
            // (CCharacterDBO::CreateCharacterIntoDB — AppearanceBlob.cs).
            var app = Eden.Core.Game.AppearanceBlob.Encode(
                new Eden.Core.Game.AppearanceBlob.Appearance(
                    gender, hair, face, hairColor, skinColor, eyes, eyes2));

            var entity = new PlayerCharacterEntity
            {
                GivenName = name,
                AccountId = accountId,
                AccountName = accountName,
                RaceId = race,
                ClassId = cls,
                GenderId = gender,
                HairStyle = hair,
                FaceStyle = face,
                HairColor = hairColor,
                SkinColor = skinColor,
                EyesColor = eyes,
                EyesColor2 = eyes2,
                NodeId = 2,                     // spawn natif (mêmes constantes que le flux in-game)
                PositionX = 332.187988,
                PositionY = 298.277008,
                PositionZ = 0.0,
                FacingDirection = 2.356190,
                Level = 1,
                ReviveAreaId = 131075,
                AppearanceData = app,
                CreationFlag = flag,
            };

            await using var fnaccountConn = accountDb.CreateConnection();
            var id = await characters.CreateCharacterNativeAsync(fnaccountConn, entity, flag);
            Console.WriteLine($"personnage créé : '{name}' id={id} (compte '{accountName}', race={race} class={cls})");
            return 0;
        }
        default:
            return Unknown($"char {sub[0]}");
    }
}

// ---------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------

/// <summary>Codes identiques au flux natif NCID 4/32 d'Eden.World.</summary>
async Task<short> NameCheckCode(string name)
{
    if (name.Length > 14) return 8;
    if (name.Length < 2) return 9;
    if (!IsLegalCharName(name)) return 10;
    if (await characters.GetCharacterByNameAsync(name) is not null) return 3;
    return 0;
}

/// <summary>Règle native : 2-14 caractères, lettres et chiffres uniquement.</summary>
static bool IsLegalCharName(string name)
{
    if (name.Length < 2 || name.Length > 14) return false;
    foreach (var ch in name)
        if (!char.IsLetterOrDigit(ch)) return false;
    return true;
}

/// <summary>Résout --account qui accepte un username ou un id numérique.</summary>
async Task<(int id, string name)> ResolveAccount(string account)
{
    if (int.TryParse(account, out var id))
    {
        var byId = await accounts.GetAccountByIdAsync(id)
            ?? throw new InvalidOperationException($"compte id={id} introuvable");
        return (byId.Id, byId.Username);
    }
    var byName = await accounts.GetAccountByUsernameAsync(account)
        ?? throw new InvalidOperationException($"compte '{account}' introuvable — créez-le d'abord : account create");
    return (byName.Id, byName.Username);
}

static string? Opt(string[] args, string key)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i] == key) return args[i + 1];
    return null;
}

static int? IntOpt(string[] args, string key)
    => Opt(args, key) is { } v ? int.Parse(v) : null;

static string Missing(string key)
    => throw new InvalidOperationException($"argument requis : {key}");

static int Unknown(string cmd)
{
    Console.Error.WriteLine($"commande inconnue : {cmd}");
    Usage();
    return 1;
}

static void Usage() => Console.WriteLine(
    """
    eden-admin — administration comptes/personnages (flux natifs)

      account create --user <u> --pass <p> [--realname <n>]
      account list
      char create --account <user|id> --name <n> --race <r> --class <c>
                  [--gender 0|1] [--hair n] [--face n] [--hair-color n]
                  [--skin-color n] [--eyes n] [--eyes2 n] [--flag n]
      char list --account <user|id>
      char name-check --name <n>

    races natives : 1=Humain 2=Zumi 3=Anuran 4=Ursun 5=Torturran
    """);

public partial class Program { }
