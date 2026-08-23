using DocumentIntelligence.Api.Services;

namespace DocumentIntelligence.Tests;

public class QualityDetectorTests
{
    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.jpeg")]
    [InlineData("photo.png")]
    [InlineData("photo.tiff")]
    [InlineData("photo.bmp")]
    [InlineData("photo.webp")]
    public void Detect_ImageExtension_ReturnsPhoto(string filename)
    {
        var result = QualityDetector.Detect(filename, null);
        Assert.Equal(DocumentQualityTier.Photo, result);
    }

    [Fact]
    public void Detect_PdfWithLongText_ReturnsDigitalPdf()
    {
        var text = new string('a', 500);
        var result = QualityDetector.Detect("document.pdf", text);
        Assert.Equal(DocumentQualityTier.DigitalPdf, result);
    }

    [Fact]
    public void Detect_PdfWithShortText_ReturnsScannedPdf()
    {
        var text = new string('a', 499);
        var result = QualityDetector.Detect("document.pdf", text);
        Assert.Equal(DocumentQualityTier.ScannedPdf, result);
    }

    [Fact]
    public void Detect_PdfWithNullText_ReturnsScannedPdf()
    {
        var result = QualityDetector.Detect("document.pdf", null);
        Assert.Equal(DocumentQualityTier.ScannedPdf, result);
    }

    [Fact]
    public void Detect_PdfWithWhitespaceOnlyText_ReturnsScannedPdf()
    {
        var text = new string(' ', 600);
        var result = QualityDetector.Detect("document.pdf", text);
        Assert.Equal(DocumentQualityTier.ScannedPdf, result);
    }

    [Fact]
    public void Detect_UnknownExtension_ReturnsPhoto()
    {
        var result = QualityDetector.Detect("file.docx", null);
        Assert.Equal(DocumentQualityTier.Photo, result);
    }

    [Fact]
    public void Detect_PdfExactThreshold_ReturnsDigitalPdf()
    {
        var text = new string('x', 500);
        var result = QualityDetector.Detect("report.PDF", text);
        Assert.Equal(DocumentQualityTier.DigitalPdf, result);
    }
}
