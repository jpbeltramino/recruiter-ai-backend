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
        if (!string.IsNullOrWhiteSpace(plainText))
            return plainText.Trim();

        if (!string.IsNullOrWhiteSpace(base64Pdf))
            return ExtractTextFromBase64(base64Pdf);

        throw new ArgumentException("Debe proporcionar texto del CV o un PDF en base64.");
    }
}
