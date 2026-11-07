using Eden.Core.Game.Modifiers;
using Eden.Zone.Features.Modifiers;

namespace Eden.Tests.Modifiers;

/// <summary>Le statut VIP lu par le gameplay : liste mémoire → contexte → bonus du moteur (F4).</summary>
public class VipDirectoryTests
{
    [Fact]
    public void Directory_ReflectsSetAccounts()
    {
        VipDirectory.Set(new[] { 7, 9 });
        Assert.True(VipDirectory.IsVip(7));
        Assert.False(VipDirectory.IsVip(8));
        VipDirectory.Set(Array.Empty<int>());
        Assert.False(VipDirectory.IsVip(7));
    }

    [Fact]
    public void VipAccount_GetsBonuses_NonVipDoesNot()
    {
        var engine = new ModifierResolver(new IModifierSource[]
            { new VipModifierSource(VipModifierSource.DefaultRules) });
        VipDirectory.Set(new[] { 7 });
        var vip = new ModifierContext(IsVip: VipDirectory.IsVip(7));
        var normal = new ModifierContext(IsVip: VipDirectory.IsVip(8));
        Assert.Equal(0.75, engine.Resolve(ModifierKeys.CraftCastSeconds, vip), 6);
        Assert.Equal(1.0, engine.Resolve(ModifierKeys.CraftCastSeconds, normal), 6);
        VipDirectory.Set(Array.Empty<int>());
    }
}
