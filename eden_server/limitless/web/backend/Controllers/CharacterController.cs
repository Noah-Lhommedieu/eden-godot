using System.Security.Claims;
using Eden.Core.Database.Entities;
using Eden.Core.Database.Repositories;
using Eden.Core.Game;
using Limitless.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Limitless.Api.Controllers;

/// <summary>Création/liste de personnages — réutilise le repo natif
/// exact (<see cref="CharacterRepository.CreateCharacterNativeAsync"/>)
/// : player_characters + player_classlist {1,2,3,4,5,19} + 23
/// spellmasters + items de départ + character_number, en une
/// transaction — identique à la création in-game (Eden.World NCID 4).</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class CharacterController : ControllerBase
{
    private readonly IConfiguration _cfg;
    public CharacterController(IConfiguration cfg) => _cfg = cfg;

    private int AccountId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string AccountName() => User.Identity?.Name ?? "";

    public record CreateChar(string Name, int Race, int ClassId,
        int Gender, int Hair, int Face, int HairColor,
        int SkinColor, int Eyes, int Eyes2, byte Flag);

    [HttpPost]
    public async Task<IActionResult> Create(CreateChar r)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 16)
            return BadRequest(new { error = "nom invalide" });
        var gameCs = _cfg.GetConnectionString("Game")
            ?? "Host=127.0.0.1;Port=5432;Database=fndb1;Username=postgres;Password=CHANGE_ME_USE_ENV";
        var accCs = _cfg.GetConnectionString("Account")
            ?? "Host=127.0.0.1;Port=5432;Database=fnaccount;Username=postgres;Password=CHANGE_ME_USE_ENV";

        await using var ds = NpgsqlDataSource.Create(gameCs);
        var repo = new CharacterRepository(ds);
        if (await repo.GetCharacterCountAsync(AccountId()) >= 3)
            return Conflict(new { error = "3 personnages max" });
        if (await repo.GetCharacterByNameAsync(r.Name) != null)
            return Conflict(new { error = "nom pris" });

        var entity = new PlayerCharacterEntity
        {
            GivenName = r.Name,
            AccountId = AccountId(), AccountName = AccountName(),
            RaceId = r.Race, ClassId = r.ClassId, GenderId = r.Gender,
            HairStyle = r.Hair, FaceStyle = r.Face,
            HairColor = r.HairColor, SkinColor = r.SkinColor,
            EyesColor = r.Eyes, EyesColor2 = r.Eyes2,
            NodeId = 2,
            PositionX = 332.187988, PositionY = 298.277008,
            PositionZ = 0.0, FacingDirection = 2.356190,
            Level = 1, ReviveAreaId = 131075,
            AppearanceData = AppearanceBlob.Encode(
                new AppearanceBlob.Appearance(
                    (byte)r.Gender, (byte)r.Hair, (byte)r.Face,
                    (byte)r.HairColor, (byte)r.SkinColor,
                    (byte)r.Eyes, (byte)r.Eyes2)),
            CreationFlag = r.Flag,
        };

        await using var accDb = NpgsqlDataSource.Create(accCs);
        await using var fnConn = accDb.CreateConnection();
        var id = await repo.CreateCharacterNativeAsync(fnConn, entity,
            r.Flag);
        return Ok(new { id, name = r.Name });
    }
}
