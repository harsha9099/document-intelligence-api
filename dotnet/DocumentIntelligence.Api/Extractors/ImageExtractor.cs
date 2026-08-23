using SkiaSharp;

namespace DocumentIntelligence.Api.Extractors;

public class ImageExtractor : IImageExtractor
{
    private readonly ILogger<ImageExtractor> _logger;

    public ImageExtractor(ILogger<ImageExtractor> logger)
    {
        _logger = logger;
    }

    public string ExtractText(byte[] fileBytes)
    {
        try
        {
            // Tesseract OCR — requires tessdata to be available on the system.
            // Falls back gracefully if Tesseract is not installed.
            using var engine = new Tesseract.TesseractEngine("./tessdata", "eng", Tesseract.EngineMode.Default);
            using var img = Tesseract.Pix.LoadFromMemory(fileBytes);
            using var page = engine.Process(img);
            return page.GetText();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tesseract OCR failed, falling back to LLM vision");
            return string.Empty;
        }
    }

    public byte[] PrepareForLlm(byte[] fileBytes)
    {
        using var bitmap = SKBitmap.Decode(fileBytes);
        if (bitmap == null)
            return fileBytes;

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}
