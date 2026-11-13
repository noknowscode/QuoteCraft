using QuoteCraft.Helpers;
using QuoteCraft.Models;
using Xunit;

namespace QuoteCraft.Tests.Helpers;

public class ClientLinkTests
{
    private static readonly ClientEntity Bob = new() { Id = "bob", Name = "Bob Johnson" };
    private static readonly ClientEntity Sarah = new() { Id = "sarah", Name = "Sarah Williams" };
    private static readonly ClientEntity[] Clients = [Bob, Sarah];

    [Fact]
    public void A_new_name_links_the_client_with_that_name()
    {
        // The editor used to keep Bob's id, so Bob's count and email followed a quote now made out to Sarah.
        Assert.Equal("sarah", ClientLink.Resolve("sarah williams ", "Bob Johnson", "bob", Clients));
    }

    [Fact]
    public void A_name_no_client_has_links_to_none()
    {
        Assert.Null(ClientLink.Resolve("Walk-in customer", "Bob Johnson", "bob", Clients));
    }

    [Fact]
    public void An_unchanged_name_keeps_its_client_after_the_client_was_renamed()
    {
        var renamed = new ClientEntity { Id = "bob", Name = "Robert Johnson" };

        Assert.Equal("bob", ClientLink.Resolve("Bob Johnson", "Bob Johnson", "bob", [renamed, Sarah]));
    }

    [Fact]
    public void An_empty_name_links_to_none()
    {
        Assert.Null(ClientLink.Resolve("  ", "Bob Johnson", "bob", Clients));
    }

    [Fact]
    public void A_typed_name_links_once_the_client_exists()
    {
        Assert.Equal("bob", ClientLink.Resolve("Bob Johnson", "Bob Johnson", null, Clients));
    }
}
