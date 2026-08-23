using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace DocumentIntelligence.Api.Extractors;

public class PdfExtractor : IPdfExtractor
{
    public string ExtractText(byte[] fileBytes)
    {
        using var document = PdfDocument.Open(fileBytes);
        var pages = new List<string>();

        foreach (var page in document.GetPages())
        {
            var text = page.Text;
            if (!string.IsNullOrWhiteSpace(text))
                pages.Add(text);
        }

        return string.Join("\n\n", pages);
    }

    public List<byte[]> ExtractPageImages(byte[] fileBytes)
    {
        // PdfPig doesn't render pages to images natively.
        // For vision-based extraction, we pass the raw PDF bytes to the LLM
        // or use SkiaSharp for rendering if needed.
        // Returning empty — the service layer will send the raw file to vision-capable LLMs.
        return [];
    }
}
