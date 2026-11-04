using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using QuoteCraft.Helpers;
using QuoteCraft.Models;
using QuoteCraft.Services;
using Xunit;

namespace QuoteCraft.Tests.Services;

/// <summary>The SkiaSharp PDF layout (docs/DECISIONS-production-readiness.md, D12), set in the app's own DM Sans.</summary>
public class PdfGeneratorTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    // The app's fonts reach the test output with the app's assets.
    private static readonly Lazy<Task<PdfFonts>> Fonts = new(() => PdfFonts.LoadAsync(name =>
        Task.FromResult<Stream>(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", name)))));

    private static BusinessProfileEntity Profile() => new()
    {
        BusinessName = "Northside Electric",
        Phone = "(555) 010-2030",
        Email = "office@northside.example",
        Address = "12 Harbour Street, Springfield",
        CurrencyCode = "USD",
        CustomFooter = "Payment due within 30 days of acceptance.",
    };

    private static QuoteEntity Quote(int items) => new()
    {
        QuoteNumber = "QC-2026-0042",
        Title = "Kitchen rewire",
        ClientName = "Dana Whitfield",
        TaxRate = 8.25m,
        ValidUntil = GeneratedAt.AddDays(14),
        Notes = "Work starts the Monday after acceptance.\nPermits are included.",
        LineItems = Enumerable.Range(1, items)
            .Select(i => new LineItemEntity
            {
                Description = $"Item {i}: replace a circuit, with the breaker, the cable run and the outlets it feeds",
                UnitPrice = 1234.50m,
                Quantity = i % 3 + 1,
                SortOrder = i,
            })
            .ToList(),
    };

    private static async Task<byte[]> RenderAsync(QuoteEntity quote, bool watermark = false) =>
        QuotePdf.Render(quote, Profile(), [], watermark, GeneratedAt, await Fonts.Value);

    /// <summary>Page objects in the file (SkiaSharp writes them uncompressed).</summary>
    private static int PageCount(byte[] pdf) =>
        Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b(?!s)").Count;

    [Fact]
    public async Task The_generator_writes_a_pdf()
    {
        var pdf = await RenderAsync(Quote(3));

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal(1, PageCount(pdf));
    }

    [Fact]
    public async Task Many_line_items_break_onto_more_pages()
    {
        Assert.True(PageCount(await RenderAsync(Quote(60))) > 1);
    }

    [PdfToTextFact]
    public async Task Amounts_read_as_the_app_formats_them()
    {
        var quote = Quote(3);

        var text = PdfToText(await RenderAsync(quote));

        Assert.Contains($"{Money.Format(quote.Subtotal, "USD")} USD", text);
        Assert.Contains($"Tax ({Money.FormatRate(quote.TaxRate)}%):", text);
        Assert.Contains(Money.Format(quote.TaxAmount, "USD"), text);
        Assert.Contains($"{Money.Format(quote.Total, "USD")} USD", text);
        Assert.Contains(Money.Format(1234.50m, "USD"), text);
    }

    [PdfToTextFact]
    public async Task Every_page_repeats_the_header_and_the_table_header()
    {
        var pages = PdfToText(await RenderAsync(Quote(60))).Split('\f', StringSplitOptions.RemoveEmptyEntries);

        Assert.True(pages.Length > 1);
        Assert.All(pages, page =>
        {
            Assert.Contains("QUOTE", page);
            Assert.Contains("#QC-2026-0042", page);
            Assert.Contains("Description", page);
            Assert.Contains("Generated on Nov 03, 2026", page);
        });
        Assert.Contains("Notes", pages[^1]);
        Assert.Contains("Permits are included.", pages[^1]);
    }

    [PdfToTextFact]
    public async Task The_header_shows_the_valid_until_day_set_in_the_editor()
    {
        var quote = Quote(1);
        quote.ValidUntil = ValidUntilDate.FromPicker(new DateTimeOffset(new DateTime(2026, 12, 1)));

        Assert.Contains("Valid Until: Dec 01, 2026", PdfToText(await RenderAsync(quote)));
    }

    [PdfToTextFact]
    public async Task The_watermark_shows_only_when_the_plan_has_one()
    {
        Assert.Contains("Created with QuoteCraft", PdfToText(await RenderAsync(Quote(1), watermark: true)));
        Assert.DoesNotContain("Created with QuoteCraft", PdfToText(await RenderAsync(Quote(1), watermark: false)));
    }

    private static string PdfToText(byte[] pdf)
    {
        var path = Path.Combine(Path.GetTempPath(), $"quotecraft-test-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, pdf);
        try
        {
            using var process = Process.Start(new ProcessStartInfo("pdftotext", [path, "-"])
            {
                RedirectStandardOutput = true,
            })!;
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return text;
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>A test that reads the PDF's text with poppler's pdftotext; skipped where it is not installed.</summary>
public sealed class PdfToTextFactAttribute : FactAttribute
{
    public PdfToTextFactAttribute()
    {
        var found = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, "pdftotext")) || File.Exists(Path.Combine(dir, "pdftotext.exe")));
        if (!found)
        {
            Skip = "pdftotext (poppler-utils) is not installed.";
        }
    }
}
