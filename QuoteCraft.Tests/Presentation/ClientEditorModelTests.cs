using QuoteCraft.Models;
using QuoteCraft.Presentation;
using Uno.Extensions.Reactive;
using Xunit;

namespace QuoteCraft.Tests.Presentation;

/// <summary>The client dialog: its headings, its checks, and what Save writes and returns.</summary>
public class ClientEditorModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static async Task<ClientEditorModel> Create(ModelHarness h, ClientEntity client, bool isNew)
    {
        var model = new ClientEditorModel(h.Navigator, h.Db.Clients, new ClientEditRequest(client, isNew));
        _ = (await model.Name, await model.Email, await model.Phone, await model.Address, await model.ValidationError);
        return model;
    }

    [Theory]
    [InlineData(true, "Add Client")]
    [InlineData(false, "Edit Client")]
    public async Task The_heading_says_whether_a_client_is_added_or_edited(bool isNew, string heading)
    {
        using var h = new ModelHarness();

        var model = await Create(h, new ClientEntity { Name = "Dana Reyes" }, isNew);

        Assert.Equal(heading, model.Heading);
        Assert.Equal("Dana Reyes", await model.Name);
    }

    [Theory]
    [InlineData("  ", "", "Enter the client's name.")]
    [InlineData("Dana Reyes", "dana@reyes", "Enter a valid email address, or leave it empty.")]
    public async Task Save_says_what_is_wrong_and_writes_nothing(string name, string email, string message)
    {
        using var h = new ModelHarness();
        var model = await Create(h, new ClientEntity(), isNew: true);

        await model.Name.UpdateAsync(_ => name, None);
        await model.Email.UpdateAsync(_ => email, None);
        await model.Save(None);

        Assert.Equal(message, await model.ValidationError);
        Assert.Empty(await h.Db.Clients.GetAllAsync());
        Assert.Empty(h.Navigator.Requests);
    }

    [Fact]
    public async Task Save_writes_the_client_trimmed_and_returns_it()
    {
        using var h = new ModelHarness();
        var model = await Create(h, new ClientEntity(), isNew: true);

        await model.Name.UpdateAsync(_ => " Dana Reyes ", None);
        await model.Email.UpdateAsync(_ => "dana@reyes.example ", None);
        await model.Phone.UpdateAsync(_ => "555-0101", None);
        await model.Save(None);

        var stored = Assert.Single(await h.Db.Clients.GetAllAsync());
        Assert.Equal(("Dana Reyes", "dana@reyes.example", "555-0101"), (stored.Name, stored.Email, stored.Phone));
        Assert.Equal(stored.Id, Assert.IsType<ClientEntity>(h.Navigator.BackResult).Id);
    }

    [Fact]
    public async Task An_edit_keeps_the_client()
    {
        using var h = new ModelHarness();
        var client = await h.AddClientAsync("Dana Reyes", "dana@reyes.example");
        var model = await Create(h, (await h.Db.Clients.GetByIdAsync(client.Id))!, isNew: false);

        await model.Name.UpdateAsync(_ => "Dana Reyes-Ortiz", None);
        await model.Save(None);

        var stored = Assert.Single(await h.Db.Clients.GetAllAsync());
        Assert.Equal((client.Id, "Dana Reyes-Ortiz", "dana@reyes.example"), (stored.Id, stored.Name, stored.Email));
    }
}
