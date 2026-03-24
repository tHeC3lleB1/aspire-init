using AspireInit.Contracts;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AspireInit.AgentWorker.Agents;

public sealed class DocumentAgent(Kernel kernel, ILogger<DocumentAgent> logger)
{
    private const string SystemPrompt = """
        You are a document processing agent. Your job is to analyse uploaded documents.

        For every document you receive, you MUST complete ALL of the following steps in order:

        1. Call UpdateDocumentStatus with status=Processing to indicate you have started.
        2. Call ParseDocument to extract the raw text from the document.
        3. Using the extracted text, determine:
           a. Document type — one of: Invoice, Contract, Report, Letter, Form, Other
           b. Named entities — a JSON object with arrays: people, organizations, locations, dates, amounts
           c. A concise 2–3 sentence summary
        4. Call StoreResult with the documentId, documentType, summary, entitiesJson, and rawText.

        If ParseDocument returns an error or the text is empty, call UpdateDocumentStatus with status=Failed
        and an appropriate error message, then stop.

        Always complete all steps. Do not skip any step. Be precise and factual.
        """;

    public async Task ProcessAsync(DocumentUploadedMessage message, CancellationToken ct)
    {
        logger.LogInformation("Agent starting: DocumentId={DocumentId} File={FileName}",
            message.DocumentId, message.FileName);

        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();

        history.AddSystemMessage(SystemPrompt);
        history.AddUserMessage(
            $"Process document. ID: {message.DocumentId}, " +
            $"filename: {message.FileName}, " +
            $"content-type: {message.ContentType}");

        var settings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        try
        {
            var response = await chatService.GetChatMessageContentAsync(
                history, settings, kernel, ct);

            logger.LogInformation("Agent finished DocumentId={DocumentId}. Agent response: {Response}",
                message.DocumentId, response.Content);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent failed for DocumentId={DocumentId}", message.DocumentId);
            throw;
        }
    }
}
