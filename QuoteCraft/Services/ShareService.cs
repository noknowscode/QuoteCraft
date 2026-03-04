using System.Diagnostics;

namespace QuoteCraft.Services;

public interface IShareService
{
    Task ShareQuotePdfAsync(QuoteEntity quote);
    Task GenerateAndDownloadPdfAsync(QuoteEntity quote);
    Task MarkAsSentAsync(QuoteEntity quote);
}

public class ShareService : IShareService
{
    private readonly IPdfGenerator _pdfGenerator;
    private readonly IQuoteRepository _quoteRepo;
    private readonly ILogger<ShareService> _logger;

    public ShareService(IPdfGenerator pdfGenerator, IQuoteRepository quoteRepo, ILogger<ShareService> logger)
    {
        _pdfGenerator = pdfGenerator;
        _quoteRepo = quoteRepo;
        _logger = logger;
    }

    public async Task ShareQuotePdfAsync(QuoteEntity quote)
    {
        await GenerateAndDownloadPdfAsync(quote);
        await MarkAsSentAsync(quote);
    }

    public async Task GenerateAndDownloadPdfAsync(QuoteEntity quote)
    {
        // Ensure line items are loaded
        if (quote.LineItems.Count == 0)
            quote.LineItems = await _quoteRepo.GetLineItemsAsync(quote.Id);

        var filePath = await _pdfGenerator.GenerateQuotePdfAsync(quote);

#if __BROWSERWASM__
        // WASM: trigger a browser download
        await DownloadFileInBrowserAsync(filePath, $"{quote.QuoteNumber}.pdf");
#else
        // Desktop/Mobile: open the PDF with the system's default application
        OpenFileWithDefaultApp(filePath);
#endif
    }

    public async Task MarkAsSentAsync(QuoteEntity quote)
    {
        if (quote.Status == QuoteStatus.Draft)
        {
            var fresh = await _quoteRepo.GetByIdAsync(quote.Id);
            if (fresh != null)
            {
                fresh.Status = QuoteStatus.Sent;
                fresh.SentAt = DateTimeOffset.UtcNow;
                await _quoteRepo.SaveAsync(fresh);
            }
        }
    }

#if !__BROWSERWASM__
    private void OpenFileWithDefaultApp(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Shell execute failed for {FilePath}, trying platform fallback", filePath);
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start("explorer.exe", $"\"{filePath}\"");
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", filePath);
                }
                else if (OperatingSystem.IsLinux())
                {
                    Process.Start("xdg-open", filePath);
                }
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "All file open methods failed for {FilePath}", filePath);
            }
        }
    }
#endif

#if __BROWSERWASM__
    private static async Task DownloadFileInBrowserAsync(string filePath, string fileName)
    {
        var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
        var base64 = Convert.ToBase64String(bytes);

        // Use JS interop to trigger browser download
        var js = $@"
            (function() {{
                var byteChars = atob('{base64}');
                var byteArray = new Uint8Array(byteChars.length);
                for (var i = 0; i < byteChars.length; i++) {{
                    byteArray[i] = byteChars.charCodeAt(i);
                }}
                var blob = new Blob([byteArray], {{ type: 'application/pdf' }});
                var url = URL.createObjectURL(blob);
                var a = document.createElement('a');
                a.href = url;
                a.download = '{fileName}';
                document.body.appendChild(a);
                a.click();
                document.body.removeChild(a);
                URL.revokeObjectURL(url);
            }})();
        ";

        await Uno.Foundation.WebAssemblyRuntime.InvokeAsync(js);
    }
#endif
}
