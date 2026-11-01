using QuoteCraft.Data.Remote;
using Xunit;

namespace QuoteCraft.Tests.Services;

public class SupabaseConfigTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("https://abc.supabase.co", "")]
    [InlineData("", "anon-key")]
    [InlineData("not a url", "anon-key")]
    [InlineData("ftp://abc.supabase.co", "anon-key")]
    public void IsNotConfigured_WithoutAUrlAndAnAnonKey(string? url, string? anonKey)
    {
        var config = new SupabaseConfig(url, anonKey);

        Assert.False(config.IsConfigured);
        Assert.Equal(string.Empty, config.Url);
        Assert.Equal(string.Empty, config.AnonKey);
    }

    [Fact]
    public void IsConfigured_WithAUrlAndAnAnonKey()
    {
        var config = new SupabaseConfig(" https://abc.supabase.co/ ", " anon-key ");

        Assert.True(config.IsConfigured);
        Assert.Equal("https://abc.supabase.co", config.Url);
        Assert.Equal("anon-key", config.AnonKey);
    }

    [Fact]
    public void AcceptsALocalSupabaseOverHttp()
    {
        var config = new SupabaseConfig("http://127.0.0.1:54321", "anon-key");

        Assert.True(config.IsConfigured);
        Assert.Equal("http://127.0.0.1:54321", config.Url);
    }

    [Fact]
    public void EnsureConfigured_Throws_WhenNotConfigured()
    {
        var config = new SupabaseConfig(null, null);

        Assert.Throws<InvalidOperationException>(config.EnsureConfigured);
    }

    [Fact]
    public void CustomerPortalUrl_NeedsHttpsAndAConfiguredProject()
    {
        Assert.Null(new SupabaseConfig(null, null, "https://billing.stripe.com/p/login/x").CustomerPortalUrl);
        Assert.Null(new SupabaseConfig("https://abc.supabase.co", "k", "http://billing.example").CustomerPortalUrl);
        Assert.Equal(
            "https://billing.stripe.com/p/login/x",
            new SupabaseConfig("https://abc.supabase.co", "k", "https://billing.stripe.com/p/login/x").CustomerPortalUrl);
    }
}
