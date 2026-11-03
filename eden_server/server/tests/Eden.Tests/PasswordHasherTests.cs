using Eden.Core.Security;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Vecteurs du hachage Argon2id/PHC (<see cref="PasswordHasher"/>) —
/// format $argon2id$v=19$m=65536,t=3,p=1$sel$hash, verify ok/ko,
/// robustesse aux PHC malformés et canonicalisation du credential wire.
/// </summary>
public class PasswordHasherTests
{
    [Fact]
    public void Test_Md5Hex_KnownVector()
    {
        // Vecteur de référence : login-poc.sql seede tb_user.pwd =
        // md5('adminmdp') = 17c725af38777cf3ac4bf34e39f44caa.
        Assert.Equal("17c725af38777cf3ac4bf34e39f44caa",
            PasswordHasher.Md5Hex("adminmdp"));
    }

    [Fact]
    public void Test_HashWireCredential_PhcFormat()
    {
        var phc = PasswordHasher.HashWireCredential("credential");

        Assert.StartsWith("$argon2id$v=19$m=65536,t=3,p=1$", phc);
        Assert.True(PasswordHasher.IsArgon2Phc(phc));

        // PHC : ["", "argon2id", "v=19", "m=…,t=…,p=…", sel b64, hash b64]
        var parts = phc.Split('$');
        Assert.Equal(6, parts.Length);
        var salt = Convert.FromBase64String(Pad(parts[4]));
        var hash = Convert.FromBase64String(Pad(parts[5]));
        Assert.Equal(PasswordHasher.SaltSize, salt.Length);   // sel 16B
        Assert.Equal(PasswordHasher.HashSize, hash.Length);   // hash 32B

        static string Pad(string s) => s.PadRight((s.Length + 3) & ~3, '=');
    }

    [Fact]
    public void Test_HashWireCredential_UniqueSalts()
    {
        var h1 = PasswordHasher.HashWireCredential("credential");
        var h2 = PasswordHasher.HashWireCredential("credential");
        Assert.NotEqual(h1, h2); // sel aléatoire 16B
    }

    [Fact]
    public void Test_Verify_RoundTrip()
    {
        var phc = PasswordHasher.HashWireCredential("17c725af38777cf3ac4bf34e39f44caa");
        Assert.True(PasswordHasher.Verify("17c725af38777cf3ac4bf34e39f44caa", phc));
        Assert.False(PasswordHasher.Verify("17c725af38777cf3ac4bf34e39f44cab", phc));
        Assert.False(PasswordHasher.Verify("autre", phc));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-phc")]
    [InlineData("$argon2id$")]
    [InlineData("$argon2i$v=19$m=65536,t=3,p=1$c2FsdA$aGFzaA")]   // variante argon2i refusée
    [InlineData("$argon2id$v=19$m=65536,t=3,p=1$")]                 // tronqué
    [InlineData("$argon2id$v=19$m=65536,t=3,p=1$!!!!$aGFzaA")]      // b64 invalide
    [InlineData("$argon2id$v=19$m=0,t=3,p=1$c2FsdA$aGFzaA")]        // params nuls refusés
    public void Test_Verify_MalformedPhc_ReturnsFalse_NoThrow(string? phc)
    {
        Assert.False(PasswordHasher.Verify("credential", phc));
    }

    [Fact]
    public void Test_Verify_TamperedHash_ReturnsFalse()
    {
        var phc = PasswordHasher.HashWireCredential("credential");
        // Flip un caractère au milieu de l'empreinte encodée — le
        // dernier char b64 de 32B ne porte que 4 bits réels (+2 bits de
        // padding), le modifier pouvait ne rien changer au hash décodé.
        var mid = phc.Length - 8;
        var tampered = phc[..mid] + (phc[mid] == 'A' ? 'B' : 'A') + phc[(mid + 1)..];
        Assert.False(PasswordHasher.Verify("credential", tampered));
    }

    [Fact]
    public void Test_CanonicalWireCredential()
    {
        // 32-hex = forme md5 envoyée par Godot → minuscules.
        Assert.Equal("17c725af38777cf3ac4bf34e39f44caa",
            PasswordHasher.CanonicalWireCredential("17C725AF38777CF3AC4BF34E39F44CAA"));
        // plaintext → md5 (forme canonique couverte par le PHC).
        Assert.Equal(PasswordHasher.Md5Hex("secret"),
            PasswordHasher.CanonicalWireCredential("secret"));
        // 32 chars non-hex = plaintext → md5.
        Assert.Equal(PasswordHasher.Md5Hex("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz"),
            PasswordHasher.CanonicalWireCredential("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz"));
    }

    [Fact]
    public void Test_HashPlaintextPassword_CoversWireMd5()
    {
        // Création de compte : l'admin saisit le plaintext ; le PHC doit
        // vérifier ce que le client enverra réellement = md5(plaintext).
        var phc = PasswordHasher.HashPlaintextPassword("motdepasse");
        Assert.True(PasswordHasher.Verify(PasswordHasher.Md5Hex("motdepasse"), phc));
        Assert.False(PasswordHasher.Verify("motdepasse", phc));
    }

    [Fact]
    public void Test_IsArgon2Phc()
    {
        Assert.True(PasswordHasher.IsArgon2Phc("$argon2id$v=19$m=65536,t=3,p=1$xx$yy"));
        Assert.False(PasswordHasher.IsArgon2Phc(null));
        Assert.False(PasswordHasher.IsArgon2Phc(""));
        Assert.False(PasswordHasher.IsArgon2Phc("17c725af38777cf3ac4bf34e39f44caa"));
        Assert.False(PasswordHasher.IsArgon2Phc("$argon2i$v=19$m=8,t=1,p=1$x$y"));
    }
}
