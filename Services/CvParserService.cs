using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace RecruiterAI.Services;

public class CvParserService
{
    /// <summary>
    /// Extracts plain text from a base64-encoded PDF.
    /// </summary>
    public string ExtractTextFromBase64(string base64Pdf)
    {
        var bytes = Convert.FromBase64String(base64Pdf);
        return ExtractTextFromBytes(bytes);
    }

    /// <summary>
    /// Extracts plain text from raw PDF bytes.
    /// </summary>
    public string ExtractTextFromBytes(byte[] pdfBytes)
    {
        using var ms = new MemoryStream(pdfBytes);
        using var reader = new PdfReader(ms);
        using var document = new PdfDocument(reader);

        var sb = new System.Text.StringBuilder();

        for (int page = 1; page <= document.GetNumberOfPages(); page++)
        {
            var strategy = new SimpleTextExtractionStrategy();
            var text = PdfTextExtractor.GetTextFromPage(document.GetPage(page), strategy);
            sb.AppendLine(text);
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Returns the text as-is if provided, or extracts from PDF base64.
    /// Throws if neither is provided.
    /// </summary>
    public string Resolve(string? plainText, string? base64Pdf)
    {
        if (!string.IsNullOrWhiteSpace(base64Pdf))
            return ExtractTextFromBase64(base64Pdf);

        if (!string.IsNullOrWhiteSpace(plainText))
            return plainText.Trim();

        throw new ArgumentException("Debe proporcionar un PDF.");
    }

    public string ExtractName(byte[] pdfBytes)
    {
        try
        {
            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var document = new PdfDocument(reader);

            var firstPage = PdfTextExtractor.GetTextFromPage(
                document.GetPage(1),
                new SimpleTextExtractionStrategy()
            );

            var lines = firstPage.Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Take(10)
                .ToList();

            foreach (var line in lines)
            {
                if (line.Contains('@')) continue;
                if (line.Any(char.IsDigit)) continue;
                if (line.Length < 5 || line.Length > 60) continue;

                var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length < 2 || words.Length > 5) continue;

                var validNameWords = words.All(w =>
                    w.Length >= 2 &&
                    char.IsUpper(w[0]) &&
                    w.All(c => char.IsLetter(c) || c == '\'' || c == '-'));

                if (validNameWords) return line;
            }

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}