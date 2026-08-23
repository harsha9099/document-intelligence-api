namespace DocumentIntelligence.Api.Extractors;

public interface IImageExtractor
{
    string ExtractText(byte[] fileBytes);
    byte[] PrepareForLlm(byte[] fileBytes);
}
