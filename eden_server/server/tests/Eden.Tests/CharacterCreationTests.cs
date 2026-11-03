using Xunit;
using Eden.Core.Game.Services;
using Eden.Core.Networking.Messages.World;

namespace Eden.Tests;

public class CharacterCreationTests
{
    [Fact]
    public void CreateCharacterRequest_ValidatesNameLength()
    {
        var shortName = new CreateCharacterRequest { GivenName = "ab" };
        var longName = new CreateCharacterRequest { GivenName = "verylongnametoolong" };
        var validName = new CreateCharacterRequest { GivenName = "ValidName" };
        
        Assert.True(validName.GivenName.Length >= 3 && validName.GivenName.Length <= 16);
        Assert.True(shortName.GivenName.Length < 3);
        Assert.True(longName.GivenName.Length > 16);
    }
    
    [Fact]
    public void CreateCharacterRequest_ValidatesRaceId()
    {
        var validRace = new CreateCharacterRequest { RaceId = 1 };
        var invalidRaceLow = new CreateCharacterRequest { RaceId = 0 };
        var invalidRaceHigh = new CreateCharacterRequest { RaceId = 6 };
        
        Assert.InRange(validRace.RaceId, 1, 5);
        Assert.True(invalidRaceLow.RaceId < 1);
        Assert.True(invalidRaceHigh.RaceId > 5);
    }
    
    [Fact]
    public void CreateCharacterRequest_ValidatesGenderId()
    {
        var male = new CreateCharacterRequest { GenderId = 1 };
        var female = new CreateCharacterRequest { GenderId = 2 };
        var invalid = new CreateCharacterRequest { GenderId = 3 };
        
        Assert.InRange(male.GenderId, 1, 2);
        Assert.InRange(female.GenderId, 1, 2);
        Assert.True(invalid.GenderId < 1 || invalid.GenderId > 2);
    }
}
