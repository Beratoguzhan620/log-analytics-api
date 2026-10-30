using LogAnalytics.Core;
using LogAnalytics.Core.Sources;
using LogAnalytics.Core.Users;

namespace LogAnalytics.UnitTests;

public sealed class SourceAndUserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_source_accepts_its_key_until_it_is_revoked()
    {
        var (source, key) = Source.Create("  Checkout ", Now);

        Assert.Equal("Checkout", source.Name);
        Assert.True(source.Accepts(key));
        source.Revoke(Now);
        Assert.False(source.Accepts(key));
        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => source.Revoke(Now)).Code);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("  ")]
    public void Source_names_need_two_characters(string name) =>
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => Source.Create(name, Now)).Code);

    [Fact]
    public void A_configured_key_must_have_the_documented_shape() =>
        Assert.Throws<DomainException>(() => Source.WithKey("Sample", "not-a-key", Now));

    [Fact]
    public void Emails_are_normalized()
    {
        var user = User.Create("  Ayse.Yilmaz@Example.COM ", " Ayşe ", UserRole.Viewer, Now);

        Assert.Equal("ayse.yilmaz@example.com", user.Email);
        Assert.Equal("Ayşe", user.DisplayName);
    }

    [Theory]
    [InlineData("not-an-email", "Name")]
    [InlineData("a@example.com", " ")]
    public void Users_need_an_email_and_a_name(string email, string name) =>
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => User.Create(email, name, UserRole.Viewer, Now)).Code);
}
