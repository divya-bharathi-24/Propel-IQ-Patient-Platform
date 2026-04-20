# Task - TASK_001

## Requirement Reference

- **User Story**: US_021 — Branded PDF Confirmation Email Within 60 Seconds
- **Story Location**: `.propel/context/tasks/EP-003-I/us_021/us_021.md`
- **Acceptance Criteria**:
  - AC-1: Given my booking is confirmed, When the booking confirmation event fires, Then a PDF is generated via QuestPDF containing appointment date, start time, end time, provider specialty, clinic/location name, and booking reference number
- **Edge Cases**:
  - QuestPDF throws an exception on generation: exception is caught and logged with the appointment ID; the `IHostedService` retry job (TASK_002) is signalled to retry the full pipeline within 2 minutes

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | No |
| **Figma URL** | N/A |
| **Wireframe Status** | N/A |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | N/A |
| **Screen Spec** | N/A |
| **UXR Requirements** | N/A |
| **Design Tokens** | N/A |

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Backend | ASP.NET Core Web API | .NET 9 |
| PDF Generation | QuestPDF | 2024.x |
| Database | PostgreSQL | 16+ |
| ORM | Entity Framework Core | 9.x |
| AI/ML | N/A | N/A |
| Vector Store | N/A | N/A |
| AI Gateway | N/A | N/A |
| Mobile | N/A | N/A |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement the `IPdfConfirmationService` abstraction and its `QuestPdfConfirmationService` implementation that generates a branded PDF appointment confirmation document as a `byte[]`. Implement the `IEmailService` abstraction and its `SendGridEmailService` implementation for sending email with a PDF attachment.

**`IPdfConfirmationService`** accepts a `PdfConfirmationData` DTO (appointment date, start time, end time, provider specialty, clinic name, booking reference number) and returns `Task<byte[]>`. The QuestPDF 2024.x template layout:

- **Header section**: "Propel IQ — Appointment Confirmation" in bold with a clinic name subtitle and booking reference number (monospace font).
- **Appointment details table**: two-column layout — field label (left) and value (right) — for Date, Start Time, End Time, Provider Specialty, and Clinic/Location.
- **Footer section**: "Please arrive 10 minutes before your appointment time." standard disclaimer text.

The document uses QuestPDF's Fluent API (`Document.Create()` → `Page` → `Header`/`Content`/`Footer`) and is generated fully in-memory (no disk I/O). Font: inter/system-default; page size: A4 portrait.

**`IEmailService`** provides `Task SendEmailWithAttachmentAsync(string toEmail, string subject, string htmlBody, byte[] attachmentBytes, string attachmentFileName)`. `SendGridEmailService` uses `SendGrid.SendGridClient` with the `SendGrid.Helpers.Mail.SendGridMessage` builder. On non-2xx response from SendGrid, throws `EmailDeliveryException` (typed exception for TASK_002 orchestration to catch).

Both services are registered in DI as **scoped** in `Program.cs`/service registration extension.

## Dependent Tasks

- **US_019 / TASK_002** — `CreateBookingCommandHandler` must publish `BookingConfirmedEvent` (the trigger consumed by TASK_002 orchestration); `PdfConfirmationData` is populated from the booking response DTO.
- **US_008 (EP-DATA)** — `Notification` entity and `notifications` table must exist before TASK_002 can INSERT notification records.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `IPdfConfirmationService` | NEW | `Server/Features/Notification/Abstractions/IPdfConfirmationService.cs` |
| `PdfConfirmationData` (DTO) | NEW | `Server/Features/Notification/Models/PdfConfirmationData.cs` |
| `QuestPdfConfirmationService` | NEW | `Server/Features/Notification/Services/QuestPdfConfirmationService.cs` |
| `IEmailService` | NEW | `Server/Features/Notification/Abstractions/IEmailService.cs` |
| `SendGridEmailService` | NEW | `Server/Features/Notification/Services/SendGridEmailService.cs` |
| `EmailDeliveryException` | NEW | `Server/Common/Exceptions/EmailDeliveryException.cs` |
| `Program.cs` / DI registration | MODIFY | Register `IPdfConfirmationService` → `QuestPdfConfirmationService`; `IEmailService` → `SendGridEmailService` as scoped |

## Implementation Plan

1. **`PdfConfirmationData` DTO**:

   ```csharp
   public record PdfConfirmationData(
       string ReferenceNumber,
       string PatientName,
       DateOnly AppointmentDate,
       TimeOnly TimeSlotStart,
       TimeOnly TimeSlotEnd,
       string ProviderSpecialty,
       string ClinicName
   );
   ```

2. **`IPdfConfirmationService`**:

   ```csharp
   public interface IPdfConfirmationService
   {
       Task<byte[]> GenerateAsync(PdfConfirmationData data, CancellationToken cancellationToken = default);
   }
   ```

3. **`QuestPdfConfirmationService.GenerateAsync()`** — QuestPDF 2024.x Fluent API:

   ```csharp
   public Task<byte[]> GenerateAsync(PdfConfirmationData data, CancellationToken cancellationToken = default)
   {
       var pdfBytes = Document.Create(container =>
       {
           container.Page(page =>
           {
               page.Size(PageSizes.A4);
               page.Margin(40);

               page.Header().Column(col =>
               {
                   col.Item().Text("Propel IQ — Appointment Confirmation")
                      .Bold().FontSize(18);
                   col.Item().Text(data.ClinicName).FontSize(12);
                   col.Item().Text($"Reference: {data.ReferenceNumber}")
                      .FontFamily("Courier New").FontSize(10);
               });

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
                   Row("Date",              data.AppointmentDate.ToString("dddd, MMMM d, yyyy"));
                   Row("Start Time",        data.TimeSlotStart.ToString("h:mm tt"));
                   Row("End Time",          data.TimeSlotEnd.ToString("h:mm tt"));
                   Row("Provider Specialty",data.ProviderSpecialty);
                   Row("Location",          data.ClinicName);
               });

               page.Footer().AlignCenter()
                   .Text("Please arrive 10 minutes before your appointment time.")
                   .FontSize(9).Italic();
           });
       }).GeneratePdf();   // synchronous in-memory generation

       return Task.FromResult(pdfBytes);
   }
   ```

4. **`IEmailService`**:

   ```csharp
   public interface IEmailService
   {
       Task SendEmailWithAttachmentAsync(
           string toEmail,
           string subject,
           string htmlBody,
           byte[] attachmentBytes,
           string attachmentFileName,
           CancellationToken cancellationToken = default);
   }
   ```

5. **`SendGridEmailService.SendEmailWithAttachmentAsync()`**:

   ```csharp
   var message = new SendGridMessage();
   message.SetFrom(new EmailAddress(_options.FromEmail, "Propel IQ"));
   message.AddTo(new EmailAddress(toEmail));
   message.SetSubject(subject);
   message.AddContent(MimeType.Html, htmlBody);
   message.AddAttachment(
       attachmentFileName,
       Convert.ToBase64String(attachmentBytes),
       "application/pdf");

   var response = await _client.SendEmailAsync(message, cancellationToken);
   if ((int)response.StatusCode >= 400)
       throw new EmailDeliveryException($"SendGrid returned {response.StatusCode} for {toEmail}");
   ```

   Configuration reads `SendGrid:ApiKey` and `SendGrid:FromEmail` from `IOptions<SendGridOptions>` — never hardcoded (OWASP A02 secrets management).

6. **DI registration** in a `NotificationServiceExtensions.AddNotificationServices()` method:

   ```csharp
   services.Configure<SendGridOptions>(configuration.GetSection("SendGrid"));
   services.AddScoped<IPdfConfirmationService, QuestPdfConfirmationService>();
   services.AddScoped<IEmailService, SendGridEmailService>();
   ```

## Current Project State

```
Server/
├── Features/
│   ├── Auth/                          (US_011, US_013 — completed)
│   ├── Booking/                       (US_019 — completed)
│   └── Notification/                  ← NEW (this task)
│       ├── Abstractions/
│       │   ├── IPdfConfirmationService.cs
│       │   └── IEmailService.cs
│       ├── Models/
│       │   └── PdfConfirmationData.cs
│       └── Services/
│           ├── QuestPdfConfirmationService.cs
│           └── SendGridEmailService.cs
├── Common/
│   └── Exceptions/
│       └── EmailDeliveryException.cs  ← NEW
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Features/Notification/Abstractions/IPdfConfirmationService.cs` | Interface: `GenerateAsync(PdfConfirmationData, CancellationToken) → Task<byte[]>` |
| CREATE | `Server/Features/Notification/Models/PdfConfirmationData.cs` | DTO record: `ReferenceNumber`, `PatientName`, `AppointmentDate`, `TimeSlotStart`, `TimeSlotEnd`, `ProviderSpecialty`, `ClinicName` |
| CREATE | `Server/Features/Notification/Services/QuestPdfConfirmationService.cs` | QuestPDF 2024.x Fluent API: branded header, appointment details table, footer disclaimer; in-memory generation |
| CREATE | `Server/Features/Notification/Abstractions/IEmailService.cs` | Interface: `SendEmailWithAttachmentAsync(...)` |
| CREATE | `Server/Features/Notification/Services/SendGridEmailService.cs` | SendGrid implementation; reads `SendGrid:ApiKey` from `IOptions<SendGridOptions>`; throws `EmailDeliveryException` on non-2xx |
| CREATE | `Server/Common/Exceptions/EmailDeliveryException.cs` | Typed exception for SendGrid non-2xx responses |
| MODIFY | `Server/Program.cs` (or `NotificationServiceExtensions.cs`) | Register `IPdfConfirmationService`, `IEmailService` as scoped; configure `SendGridOptions` |

## External References

- [QuestPDF — Fluent API Documentation](https://www.questpdf.com/documentation/getting-started.html)
- [QuestPDF — Document.Create() and Page layout](https://www.questpdf.com/documentation/api-reference.html)
- [SendGrid — C# Mail Send](https://github.com/sendgrid/sendgrid-csharp)
- [TR-010 — SendGrid free tier for email delivery](design.md#TR-010)
- [OWASP A02:2021 — Cryptographic Failures / Secrets Management](https://owasp.org/Top10/A02_2021-Cryptographic_Failures/) — API keys via `IOptions<T>`, never hardcoded

## Build Commands

- Refer to: `.propel/build/backend-build.md`
- Package install: `dotnet add package QuestPDF --version 2024.*`
- Package install: `dotnet add package SendGrid`

## Implementation Validation Strategy

- [ ] Unit tests pass: `QuestPdfConfirmationService` returns non-empty `byte[]` with mocked `PdfConfirmationData`
- [ ] Unit tests pass: `SendGridEmailService` calls `SendGridClient.SendEmailAsync()` with correct `To`, `Subject`, and base64 attachment
- [ ] `SendGridEmailService` throws `EmailDeliveryException` when SendGrid returns 4xx/5xx status (tested via mocked `SendGridClient`)
- [ ] SendGrid API key is read from `IOptions<SendGridOptions>` — no hardcoded strings in code
- [ ] QuestPDF generation is fully in-memory (no temp file created on disk)

## Implementation Checklist

- [ ] Create `PdfConfirmationData` record DTO: `ReferenceNumber`, `PatientName`, `AppointmentDate` (DateOnly), `TimeSlotStart`/`TimeSlotEnd` (TimeOnly), `ProviderSpecialty`, `ClinicName`
- [ ] Implement `QuestPdfConfirmationService`: QuestPDF 2024.x `Document.Create()` → A4 page; branded header (title + clinic + reference); two-column appointment details table (Date, Start Time, End Time, Provider Specialty, Location); disclaimer footer; returns `byte[]` fully in-memory (no disk I/O)
- [ ] Implement `IEmailService` interface + `SendGridEmailService`: `SendGridMessage` builder with PDF base64 attachment; reads `SendGrid:ApiKey` and `SendGrid:FromEmail` from `IOptions<SendGridOptions>` (never hardcoded — OWASP A02); throws `EmailDeliveryException` on non-2xx SendGrid response
- [ ] Create `EmailDeliveryException` typed exception class in `Server/Common/Exceptions/`
- [ ] Register `IPdfConfirmationService` → `QuestPdfConfirmationService` and `IEmailService` → `SendGridEmailService` as scoped in `NotificationServiceExtensions.AddNotificationServices()`; configure `SendGridOptions` from `configuration.GetSection("SendGrid")`
