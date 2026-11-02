using Microsoft.UI.Xaml.Input;

namespace QuoteCraft.Presentation;

public sealed partial class ClientsPage : Page
{
    /// <summary>Access the underlying MVUX model from the generated ViewModel DataContext.</summary>
    private ClientsModel? Model => MvuxHelper.GetModel<ClientsModel>(DataContext);

    public ClientsPage()
    {
        this.InitializeComponent();
    }

    private async void AddClient_Click(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunAsync(this, "The client could not be added.", async () =>
        {
            if (Model is not { } model) return;

            if (await model.GetClientLimitMessageAsync() is { } limit)
            {
                var limitDialog = DialogHelper.BuildBannerDialog(
                    this.XamlRoot,
                    "\uE77B",
                    "Client Limit Reached",
                    limit,
                    closeButtonText: "OK",
                    primaryButtonText: "Upgrade");
                if (await limitDialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    // Plans and the upgrade live in Settings.
                    await model.OpenSettings(CancellationToken.None);
                }
                return;
            }

            await ShowClientEditorDialog(model, new ClientEntity(), isNew: true);
        });

    private async void EditClient_Click(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunAsync(this, "The client could not be opened.", async () =>
        {
            // The selection lives in the model (cards select through the list's command, taps and keys alike).
            if (Model is { } model && await model.GetSelectedClientAsync() is { } client)
                await ShowClientEditorDialog(model, client, isNew: false);
        });

    private async Task ShowClientEditorDialog(ClientsModel model, ClientEntity entity, bool isNew = false)
    {
        var nameBox = new TextBox
        {
            Header = "Name",
            Text = entity.Name ?? string.Empty,
            PlaceholderText = "Client or business name",
        };
        var emailBox = new TextBox
        {
            Header = "Email",
            Text = entity.Email ?? string.Empty,
            PlaceholderText = "email@example.com",
            InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.EmailSmtpAddress) } },
        };
        var phoneBox = new TextBox
        {
            Header = "Phone",
            Text = entity.Phone ?? string.Empty,
            PlaceholderText = "(555) 123-4567",
            InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.TelephoneNumber) } },
        };
        var addressBox = new TextBox
        {
            Header = "Address",
            Text = entity.Address ?? string.Empty,
            PlaceholderText = "Street, City, State/Province",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = (double)Application.Current.Resources["NotesMinHeight"],
        };

        var validation = DialogHelper.BuildValidationText(this);

        var fieldsPanel = new StackPanel { Spacing = (double)Application.Current.Resources["Space16"] };
        fieldsPanel.Children.Add(nameBox);
        fieldsPanel.Children.Add(emailBox);
        fieldsPanel.Children.Add(phoneBox);
        fieldsPanel.Children.Add(addressBox);
        fieldsPanel.Children.Add(validation);

        var dialog = Helpers.DialogHelper.BuildBannerDialogWithContent(
            this.XamlRoot,
            "\uE77B",
            isNew ? "Add Client" : "Edit Client",
            isNew ? "Add a new client to your contact list" : "Update client details",
            fieldsPanel,
            primaryButtonText: "Save",
            closeButtonText: "Cancel");

        dialog.PrimaryButtonClick += async (s, args) =>
        {
            var name = nameBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                DialogHelper.ShowValidation(validation, "Enter the client's name.");
                args.Cancel = true;
                return;
            }

            var email = emailBox.Text?.Trim() ?? string.Empty;
            if (email.Length > 0 && (!email.Contains('@') || !email.Contains('.')))
            {
                DialogHelper.ShowValidation(validation, "Enter a valid email address, or leave it empty.");
                args.Cancel = true;
                return;
            }

            // Saving: the dialog waits for the database before it closes.
            var deferral = args.GetDeferral();

            entity.Name = name;
            entity.Email = emailBox.Text;
            entity.Phone = phoneBox.Text;
            entity.Address = addressBox.Text;
            try
            {
                await model.SaveClientAsync(entity);
            }
            catch (Exception ex)
            {
                ErrorReporter.Log(ex, "Save client");
                DialogHelper.ShowValidation(validation, "The client could not be saved. Try again.");
                args.Cancel = true;
            }
            finally
            {
                deferral.Complete();
            }
        };

        // Save happens in the dialog (it stays open on a failure), and refreshes the list.
        await dialog.ShowAsync();
    }

    private async void DeleteClient_Click(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunAsync(this, "The client could not be deleted.", async () =>
        {
            var dialog = DialogHelper.BuildBannerDialog(
                this.XamlRoot,
                "\uE74D",
                "Delete Client?",
                "This client will be permanently removed from your contact list.",
                closeButtonText: "Cancel",
                primaryButtonText: "Delete");

            if (await dialog.ShowAsync() == ContentDialogResult.Primary && Model is { } model)
                await model.DeleteClient(CancellationToken.None);
        });

    private async void NewQuoteForClient_Click(object sender, RoutedEventArgs e) =>
        await ErrorReporter.RunAsync(this, "The quote could not be created.", async () =>
        {
            if (Model is { } model)
                await model.CreateQuoteForClient(CancellationToken.None);
        });

    private void QuoteRow_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)
        {
            e.Handled = true;
            QuoteRow_Tapped(sender, null);
        }
    }

    private async void QuoteRow_Tapped(object sender, TappedRoutedEventArgs? e)
    {
        if (sender is not FrameworkElement { DataContext: QuoteEntity quote }) return;

        await ErrorReporter.RunAsync(this, "The quote could not be opened.", async () =>
        {
            var navigator = this.Navigator() ?? throw new InvalidOperationException("The page has no navigator.");
            await navigator.NavigateRouteAsync(this, "QuoteEditor", data: quote);
        });
    }
}
