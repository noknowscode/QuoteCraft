#if QUESTPDF
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace QuoteCraft.Services;

/// <summary>
/// QuestPDF renderer. QuestPDF ships native libraries for Windows, Linux and macOS only, so this generator exists on the
/// desktop and WinAppSDK heads (QUESTPDF is defined in QuoteCraft.csproj).
/// </summary>
public class PdfGenerator : IPdfGenerator
{
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly IFeatureGateService _featureGate;
    private readonly IPhotoService _photoService;

    public PdfGenerator(IBusinessProfileRepository profileRepo, IFeatureGateService featureGate, IPhotoService photoService)
    {
        _profileRepo = profileRepo;
        _featureGate = featureGate;
        _photoService = photoService;
    }

    public bool IsSupported => true;

    public async Task<string> GenerateQuotePdfAsync(QuoteEntity quote)
    {
        try
        {
        QuestPDF.Settings.License = LicenseType.Community;

        var profile = await _profileRepo.GetAsync();
        // Amounts and rates as the app shows them (Helpers/Money).
        var currencyCode = profile.CurrencyCode;
        string Amount(decimal value) => Money.Format(value, currencyCode);
        var photos = _photoService.GetPhotos(quote.Id);
        var dir = AppStorage.Combine("exports");
        Directory.CreateDirectory(dir);

        var filePath = Path.Combine(dir, $"{quote.QuoteNumber}.pdf");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        // Logo (if uploaded)
                        if (!string.IsNullOrEmpty(profile.LogoPath) && File.Exists(profile.LogoPath))
                        {
                            row.ConstantItem(60).Height(60).Image(profile.LogoPath).FitArea();
                            row.ConstantItem(12); // spacer
                        }

                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(profile.BusinessName ?? "My Business")
                                .FontSize(18).Bold().FontColor(Colors.Blue.Darken3);
                            if (!string.IsNullOrEmpty(profile.Phone))
                                c.Item().Text(profile.Phone);
                            if (!string.IsNullOrEmpty(profile.Email))
                                c.Item().Text(profile.Email);
                            if (!string.IsNullOrEmpty(profile.Address))
                                c.Item().Text(profile.Address);
                        });

                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().Text("QUOTE").FontSize(24).Bold().FontColor(Colors.Blue.Darken3);
                            c.Item().Text($"#{quote.QuoteNumber}");
                            c.Item().Text($"Date: {DateTimeOffset.UtcNow:MMM dd, yyyy}");
                            if (quote.ValidUntil.HasValue)
                            {
                                c.Item().Text($"Valid Until: {quote.ValidUntil.Value:MMM dd, yyyy}");
                            }
                        });
                    });

                    col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().Column(col =>
                {
                    // Quote title and client
                    col.Item().PaddingBottom(8).Text(quote.Title).FontSize(14).Bold();

                    if (!string.IsNullOrEmpty(quote.ClientName))
                        col.Item().PaddingBottom(12).Text($"Client: {quote.ClientName}").FontSize(11);

                    // Line items table
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(4);
                            cols.RelativeColumn(1);
                            cols.RelativeColumn(1);
                            cols.RelativeColumn(1);
                        });

                        // Header
                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Blue.Darken3).Padding(6)
                                .Text("Description").FontColor(Colors.White).Bold();
                            header.Cell().Background(Colors.Blue.Darken3).Padding(6).AlignRight()
                                .Text("Qty").FontColor(Colors.White).Bold();
                            header.Cell().Background(Colors.Blue.Darken3).Padding(6).AlignRight()
                                .Text("Unit Price").FontColor(Colors.White).Bold();
                            header.Cell().Background(Colors.Blue.Darken3).Padding(6).AlignRight()
                                .Text("Total").FontColor(Colors.White).Bold();
                        });

                        // Rows
                        foreach (var item in quote.LineItems)
                        {
                            var bg = quote.LineItems.IndexOf(item) % 2 == 0
                                ? Colors.White : Colors.Grey.Lighten4;

                            table.Cell().Background(bg).Padding(6).Text(item.Description);
                            table.Cell().Background(bg).Padding(6).AlignRight().Text(item.Quantity.ToString());
                            table.Cell().Background(bg).Padding(6).AlignRight().Text(Amount(item.UnitPrice));
                            table.Cell().Background(bg).Padding(6).AlignRight().Text(Amount(item.LineTotal));
                        }
                    });

                    // Totals
                    col.Item().PaddingTop(12).AlignRight().Column(totals =>
                    {
                        totals.Item().Row(row =>
                        {
                            row.RelativeItem().AlignRight().Text("Subtotal:").Bold();
                            row.ConstantItem(120).AlignRight().Text($"{Amount(quote.Subtotal)} {currencyCode}");
                        });
                        totals.Item().Row(row =>
                        {
                            row.RelativeItem().AlignRight().Text($"Tax ({Money.FormatRate(quote.TaxRate)}%):");
                            row.ConstantItem(120).AlignRight().Text(Amount(quote.TaxAmount));
                        });
                        totals.Item().PaddingTop(4).Row(row =>
                        {
                            row.RelativeItem().AlignRight().Text("Total:").FontSize(14).Bold()
                                .FontColor(Colors.Blue.Darken3);
                            row.ConstantItem(120).AlignRight().Text($"{Amount(quote.Total)} {currencyCode}")
                                .FontSize(14).Bold().FontColor(Colors.Blue.Darken3);
                        });
                    });

                    // Notes
                    if (!string.IsNullOrEmpty(quote.Notes))
                    {
                        col.Item().PaddingTop(20).Text("Notes").Bold();
                        col.Item().PaddingTop(4).Text(quote.Notes);
                    }
                });

                page.Footer().Column(col =>
                {
                    if (!string.IsNullOrEmpty(profile.CustomFooter))
                    {
                        col.Item().PaddingBottom(4).Text(profile.CustomFooter)
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                    }
                    if (_featureGate.HasPdfWatermark)
                    {
                        col.Item().PaddingBottom(2).Text("Created with QuoteCraft - quotecraft.app")
                            .FontSize(8).FontColor(Colors.Grey.Darken1).AlignCenter();
                    }
                    col.Item().Text($"Generated on {DateTimeOffset.UtcNow:MMM dd, yyyy}")
                        .FontSize(7).FontColor(Colors.Grey.Lighten1).AlignCenter();
                });
            });

            // Photo appendix page (if photos exist)
            if (photos.Count > 0)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Text("Photo Attachments").FontSize(16).Bold()
                        .FontColor(Colors.Blue.Darken3);

                    page.Content().PaddingTop(16).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(1);
                            cols.RelativeColumn(1);
                        });

                        foreach (var photoPath in photos)
                        {
                            if (File.Exists(photoPath))
                            {
                                table.Cell().Padding(6).Border(1).BorderColor(Colors.Grey.Lighten2)
                                    .Image(photoPath)
                                    .FitArea();
                            }
                        }
                    });

                    page.Footer().Text($"Generated on {DateTimeOffset.UtcNow:MMM dd, yyyy}")
                        .FontSize(7).FontColor(Colors.Grey.Lighten1).AlignCenter();
                });
            }
        }).GeneratePdf(filePath);

        return filePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Most often the previous export of this quote is still open in a PDF viewer that locks it (Windows).
            throw new InvalidOperationException(
                "The PDF file could not be written. If it is open in another app, close it and try again.", ex);
        }
        catch (QuestPDF.Drawing.Exceptions.DocumentLayoutException ex)
        {
            throw new InvalidOperationException("The PDF could not be laid out.", ex);
        }
    }
}
#endif
