namespace Propel.Domain.Interfaces;

/// <summary>
/// Abstraction for generating an in-memory branded PDF appointment confirmation document.
/// Implementations use QuestPDF to produce a byte array without touching the file system.
/// </summary>
public interface IPdfConfirmationService
{
    /// <summary>
    /// Generates a branded PDF appointment confirmation document and returns the raw bytes.
    /// </summary>
    /// <param name="data">Appointment details required to populate the PDF template.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    /// <returns>A <see cref="byte"/> array containing the generated PDF.</returns>
    Task<byte[]> GenerateAsync(PdfConfirmationData data, CancellationToken cancellationToken = default);
}
