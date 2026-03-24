namespace AspireInit.DocumentAPI.Models;

public class DocumentResult
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string? DocumentType { get; set; }
    public string? Summary { get; set; }
    public string? EntitiesJson { get; set; }
    public string? RawText { get; set; }
    public DateTime CreatedAt { get; set; }

    public Document? Document { get; set; }
}
