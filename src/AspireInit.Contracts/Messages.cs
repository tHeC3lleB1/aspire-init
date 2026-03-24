namespace AspireInit.Contracts;

/// <summary>Published by DocumentAPI when a file is uploaded and ready for processing.</summary>
public record DocumentUploadedMessage(
    Guid DocumentId,
    string FileName,
    string ContentType);

/// <summary>Published by AgentWorker when processing finishes (success or failure).</summary>
public record DocumentProcessedMessage(
    Guid DocumentId,
    string Status,
    Guid? ResultId,
    string? Error);
