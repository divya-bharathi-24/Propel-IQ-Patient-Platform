using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Propel.Api.Gateway.Infrastructure.Pdf;

/// <summary>
/// QuestPDF 2024.x implementation of <see cref="IPdfConfirmationService"/>.
/// Generates a branded A4 appointment confirmation document fully in-memory — no disk I/O.
/// Layout: branded header (title + clinic name + reference number), two-column appointment
/// details table, and a footer disclaimer (US_021, AC-1).
/// </summary>
public sealed class QuestPdfConfirmationService : IPdfConfirmationService
{
    private readonly ILogger<QuestPdfConfirmationService> _logger;

    public QuestPdfConfirmationService(ILogger<QuestPdfConfirmationService> logger)
    {
        _logger = logger;
        // QuestPDF community licence — free for open-source / evaluation use.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public Task<byte[]> GenerateAsync(PdfConfirmationData data, CancellationToken cancellationToken = default)
    {
        try
        {
            byte[] pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);

                    // ── Header ───────────────────────────────────────────────
                    page.Header().Column(col =>
                    {
                        col.Item()
                           .Text("Propel IQ \u2014 Appointment Confirmation")
                           .Bold()
                           .FontSize(18);

                        col.Item()
                           .Text(data.ClinicName)
                           .FontSize(12);

                        col.Item()
                           .Text($"Reference: {data.ReferenceNumber}")
                           .FontFamily("Courier New")
                           .FontSize(10);
                    });

                    // ── Content: two-column appointment details table ─────────
                    page.Content().PaddingTop(20).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(1);
                            cols.RelativeColumn(2);
                        });

                        void Row(string label, string value)
                        {
                            table.Cell().Text(label).Bold();
                            table.Cell().Text(value);
                        }

                        Row("Date",               data.AppointmentDate.ToString("dddd, MMMM d, yyyy"));
                        Row("Start Time",         data.TimeSlotStart.ToString("h:mm tt"));
                        Row("End Time",           data.TimeSlotEnd.ToString("h:mm tt"));
                        Row("Provider Specialty", data.ProviderSpecialty);
                        Row("Location",           data.ClinicName);
                    });

                    // ── Footer ───────────────────────────────────────────────
                    page.Footer()
                        .AlignCenter()
                        .Text("Please arrive 10 minutes before your appointment time.")
                        .FontSize(9)
                        .Italic();
                });
            }).GeneratePdf();

            return Task.FromResult(pdfBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "QuestPDF failed to generate appointment confirmation PDF for reference {ReferenceNumber}.",
                data.ReferenceNumber);
            throw;
        }
    }
}
