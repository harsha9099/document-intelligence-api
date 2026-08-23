namespace DocumentIntelligence.Api.Services;

public enum DocumentQualityTier
{
    DigitalPdf,
    ScannedPdf,
    Photo
}

public static class QualityDetector
{
    private static readonly HashSet<string> ImageExtensions = ["jpg", "jpeg", "png", "tiff", "bmp", "webp"];
    private const int DigitalPdfTextThreshold = 500;

    public static DocumentQualityTier Detect(string filename, string? extractedText)
    {
        var ext = Path.GetExtension(filename).TrimStart('.').ToLowerInvariant();

        if (ImageExtensions.Contains(ext))
            return DocumentQualityTier.Photo;

        if (ext == "pdf")
        {
            return extractedText?.Trim().Length >= DigitalPdfTextThreshold
                ? DocumentQualityTier.DigitalPdf
                : DocumentQualityTier.ScannedPdf;
        }

        return DocumentQualityTier.Photo;
    }
}
