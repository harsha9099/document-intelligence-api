namespace DocumentIntelligence.Api.Extractors;

public interface IPdfExtractor
{
    string ExtractText(byte[] fileBytes);
    List<byte[]> ExtractPageImages(byte[] fileBytes);
}
