namespace Propel.Modules.AI.Exceptions;

/// <summary>
/// Thrown by <see cref="Interfaces.IDocumentChunkingService"/> when a PDF contains no
/// extractable text (image-only / scanned document without an embedded text layer).
/// <para>
/// Caught by <c>ExtractionPipelineWorker</c> (task_004, EC-1) — the worker must:
/// <list type="bullet">
///   <item>Set <c>ClinicalDocument.ProcessingStatus = Failed</c>.</item>
///   <item>Send an extraction-failure email to the patient advising them to upload
///         a text-based PDF (EC-1).</item>
/// </list>
/// </para>
/// </summary>
public sealed class DocumentExtractionException : Exception
{
    public DocumentExtractionException(string message) : base(message)
    {
    }

    public DocumentExtractionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
