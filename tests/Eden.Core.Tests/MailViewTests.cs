using EdenClient.Protocol;
using EdenClient.Windows;
using Xunit;

namespace EdenClient.NetTests;

public class MailViewTests
{
    private static MailEntry M(int id, byte flag = 0, int item = 0,
        long gold = 0) => new()
    {
        MailId = id, SenderName = "Bob", Subject = "Hi", Flag = flag,
        AttachItemId = item, AttachGold = gold,
    };

    [Fact]
    public void ListLine_UnreadAndAttachment()
    {
        Assert.Equal("◆ Bob — Hi", MailView.ListLine(M(1)));
        Assert.Equal("◇ Bob — Hi [PJ]", MailView.ListLine(M(1, 1, 5)));
        Assert.Equal("◇ Bob — Hi [PJ]", MailView.ListLine(M(1, 1, 0, 10)));
    }

    [Fact]
    public void Date_Utc()
    {
        Assert.Equal("", MailView.Date(0));
        Assert.Equal("02/01/1970 00:00", MailView.Date(86400, true));
        Assert.Equal("02/01", MailView.Date(86400, true, "dd/MM"));
    }

    [Fact]
    public void Reply_Subject_NoDoublePrefix()
    {
        Assert.Equal("Re: Hello", MailView.ReplySubject("Hello"));
        Assert.Equal("re: Hello", MailView.ReplySubject("re: Hello"));
    }

    [Fact]
    public void TakeAll_OnlyItems()
    {
        var ids = MailView.TakeAllIds(new[] { M(1), M(2, 0, 9), M(3, 0, 0, 5), M(4, 0, 7) });
        Assert.Equal(new[] { 2, 4 }, ids);
    }

    [Theory]
    [InlineData("12", 12)]
    [InlineData("-4", 0)]
    [InlineData(null, 0)]
    [InlineData("x", 0)]
    public void ParseAmount(string? t, int e) =>
        Assert.Equal(e, MailView.ParseAmount(t));

    [Fact]
    public void Validate_And_Result()
    {
        Assert.NotNull(MailView.ValidateSend("  "));
        Assert.Null(MailView.ValidateSend("Bob"));
        Assert.Equal("Mail sent.", MailView.SendResultKey(0));
    }

    [Fact]
    public void WireShapes_Mail()
    {
        var s = new ZoneMailSend { To = "A", Title = "t", Body = "b" };
        Assert.Equal(99, s.Opcode);
        Assert.Equal(-1, s.ItemBag);
        Assert.Equal(96, new ZoneMailQuery().Opcode);
        Assert.Equal(97, new ZoneMailRead().Opcode);
        Assert.Equal(102, new ZoneMailReturn().Opcode);
    }
}
