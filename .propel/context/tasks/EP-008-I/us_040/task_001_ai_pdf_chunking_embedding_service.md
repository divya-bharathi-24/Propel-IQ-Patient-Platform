# Task - TASK_001

## Requirement Reference

- **User Story**: US_040 — AI Document Extraction RAG Pipeline
- **Story Location**: `.propel/context/tasks/EP-008-I/us_040/us_040.md`
- **Acceptance Criteria**:
  - AC-1: Document chunked into 512-token segments with 10% overlap; embeddings generated using `text-embedding-3-small`; all chunks stored in pgvector.
- **Edge Cases**:
  - EC-1: PDF has no extractable text (scanned image-only) → OCR pre-processing attempted; if OCR fails → `processingStatus = Failed`, patient notified to upload text-based PDF.

## Design References (Frontend Tasks Only)

| Reference Type         | Value |
|------------------------|-------|
| **UI Impact**          | No    |
| **Figma URL**          | N/A   |
| **Wireframe Status**   | N/A   |
| **Wireframe Type**     | N/A   |
| **Wireframe Path/URL** | N/A   |
| **Screen Spec**        | N/A   |
| **UXR Requirements**   | N/A   |
| **Design Tokens**      | N/A   |

## Applicable Technology Stack

| Layer         | Technology                        | Version    |
|---------------|-----------------------------------|------------|
| Backend       | ASP.NET Core Web API              | .NET 9     |
| AI/ML         | OpenAI text-embedding-3-small     | Latest     |
| AI Orchestration | Microsoft Semantic Kernel      | 1.x        |
| Vector Store  | pgvector (PostgreSQL extension)   | 0.7+       |
| ORM           | Entity Framework Core             | 9.x        |
| Database      | PostgreSQL                        | 16+        |
| Logging       | Serilog                           | 4.x        |
| Mobile        | N/A                               | N/A        |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type       | Value |
|----------------------|-------|
| **AI Impact**        | Yes   |
| **AIR Requirements** | AIR-R01, AIR-S01, AIR-001 |
| **AI Pattern**       | RAG   |
| **Prompt Template Path** | N/A (chunking/embedding — no prompt in this task) |
| **Guardrails Config** | `Server/PropelIQ.Clinical/AI/Guardrails/` |
| **Model Provider**   | OpenAI |

> **AI Impact = Yes** — this task implements the first phase of the RAG pipeline: chunking and embedding generation. No prompt template required at this stage; prompt construction is in task_003.

### **CRITICAL: AI Implementation Requirements**
- **MUST** apply PII redaction/de-identification to all chunk text before sending to OpenAI embedding API (AIR-S01 — HIPAA compliance)
- **MUST** enforce token budget awareness: chunk size is 512 tokens with 10% (≈51 token) overlap (AIR-R01)
- **MUST** log embedding request/response metadata (token count, model, documentId) to audit trail — redact patient name and identifiers from logs (AIR-S03)
- **MUST** handle OpenAI embedding API failures gracefully — mark document `processingStatus = Failed`, do not throw (NFR-018)

## Mobile References (Mobile Tasks Only)

| Reference Type      | Value |
|---------------------|-------|
| **Mobile Impact**   | No    |
| **Platform Target** | N/A   |
| **Min OS Version**  | N/A   |
| **Mobile Framework**| N/A   |

## Task Overview

Implement the first phase of the RAG ingestion pipeline: `IDocumentChunkingService` and `IEmbeddingGenerationService`. The chunking service reads the decrypted PDF binary from storage, attempts text extraction (using PdfPig or equivalent), falls back to OCR (Tesseract) for image-only PDFs (EC-1), splits the extracted text into 512-token segments with 10% (≈51 token) overlap per AIR-R01, and returns a list of `DocumentChunk` objects. The embedding service sends each chunk's text to the OpenAI `text-embedding-3-small` model, receives a 1536-dimension float vector, applies PII redaction before transmission (AIR-S01), and returns the vector for storage in task_002.

## Dependent Tasks

- **task_005_db_extraction_schema_migration.md** — `DocumentChunkEmbedding` table must exist before embeddings can be persisted.

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `IDocumentChunkingService` | `PropelIQ.Clinical` | CREATE |
| `DocumentChunkingService` | `PropelIQ.Clinical` | CREATE |
| `IEmbeddingGenerationService` | `PropelIQ.Clinical` | CREATE |
| `EmbeddingGenerationService` | `PropelIQ.Clinical` | CREATE |
| `IPiiRedactionService` | `PropelIQ.Clinical` | CREATE |
| `PiiRedactionService` | `PropelIQ.Clinical` | CREATE |
| `DocumentChunk` (DTO) | `PropelIQ.Clinical` | CREATE |

## Implementation Plan

1. **Implement `DocumentChunkingService`** — accept `byte[]` PDF binary as input. Use `PdfPig` library to extract text page-by-page. If text extraction yields no content (`string.IsNullOrWhiteSpace`), invoke Tesseract OCR (`Tesseract` NuGet package). If OCR also yields no content, throw `DocumentExtractionException` to signal EC-1 failure.
2. **Token-based chunking** — use `Microsoft.ML.Tokenizers` (or `TiktokenSharp`) to count tokens. Iterate text: slide a window of 512 tokens, step forward by 461 tokens (512 × 0.90) each time to achieve 10% overlap. Each chunk retains: `text`, `pageNumber` (derived from PdfPig page context), `startTokenIndex`, `endTokenIndex`.
3. **Return `List<DocumentChunk>`** — each entry: `{ Text, PageNumber, StartTokenIndex, EndTokenIndex, DocumentId }`.
4. **Implement `PiiRedactionService`** — before sending any text to OpenAI, apply regex and rule-based redaction: replace patient name patterns, DOB formats (`\d{1,2}/\d{1,2}/\d{4}`), SSN patterns, phone numbers, and email addresses with `[REDACTED]` tokens. This satisfies AIR-S01 for non-HIPAA-BAA provider paths.
5. **Implement `EmbeddingGenerationService`** — for each `DocumentChunk`, call `PiiRedactionService.Redact(chunk.Text)`, then call OpenAI `text-embedding-3-small` via Semantic Kernel's `ITextEmbeddingGenerationService`. Return `float[]` vector (1536 dimensions).
6. **Batch embedding calls** — send chunks in batches of 20 to avoid per-request overhead. Use `Task.WhenAll` within the batch; respect OpenAI rate limits with a 100ms inter-batch delay.
7. **Error handling** — if any batch embedding call fails (timeout, 5xx), the entire document processing moves to `Failed` status via the pipeline worker (task_004). Log model, documentId, batch index, and HTTP status code (without PII) using Serilog (AIR-S03).
8. **Audit log** — after successful batch, write to `AuditLog`: action = `EmbeddingGenerated`, entity = `ClinicalDocument`, entityId = documentId, details = `{ chunkCount, tokenTotal, model }` (no PII in details per AIR-S03).

### Pseudocode

```csharp
// DocumentChunkingService.cs
public async Task<List<DocumentChunk>> ChunkAsync(byte[] pdfBytes, Guid documentId, CancellationToken ct)
{
    var pages = _pdfReader.ExtractPages(pdfBytes); // PdfPig
    var fullText = string.Join(" ", pages.Select(p => p.Text));

    if (string.IsNullOrWhiteSpace(fullText))
    {
        fullText = await _ocrService.ExtractTextAsync(pdfBytes, ct); // Tesseract fallback
        if (string.IsNullOrWhiteSpace(fullText))
            throw new DocumentExtractionException(documentId, "No extractable text found");
    }

    return _tokenChunker.Chunk(fullText, chunkSize: 512, overlap: 51, documentId);
}

// EmbeddingGenerationService.cs
public async Task<List<ChunkWithEmbedding>> GenerateAsync(
    List<DocumentChunk> chunks, CancellationToken ct)
{
    var results = new List<ChunkWithEmbedding>();
    foreach (var batch in chunks.Chunk(20))
    {
        var redacted = batch.Select(c => _piiRedaction.Redact(c.Text)).ToList();
        var embeddings = await _skEmbeddingService.GenerateEmbeddingsAsync(redacted, ct);
        results.AddRange(batch.Zip(embeddings, (c, e) => new ChunkWithEmbedding(c, e)));
        await Task.Delay(100, ct); // rate limit buffer
    }
    return results;
}
```

## Current Project State

```
Server/
├── PropelIQ.Clinical/
│   ├── Services/
│   │   └── (no chunking or embedding services yet)
│   └── AI/
│       └── (no AI services yet)
└── PropelIQ.Api/
    └── Program.cs
```

> Placeholder — update with actual paths as dependent tasks complete.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/PropelIQ.Clinical/AI/Services/IDocumentChunkingService.cs` | Chunking service interface |
| CREATE | `Server/PropelIQ.Clinical/AI/Services/DocumentChunkingService.cs` | PdfPig + OCR + token-based chunker |
| CREATE | `Server/PropelIQ.Clinical/AI/Services/IEmbeddingGenerationService.cs` | Embedding service interface |
| CREATE | `Server/PropelIQ.Clinical/AI/Services/EmbeddingGenerationService.cs` | OpenAI text-embedding-3-small via Semantic Kernel |
| CREATE | `Server/PropelIQ.Clinical/AI/Services/IPiiRedactionService.cs` | PII redaction interface |
| CREATE | `Server/PropelIQ.Clinical/AI/Services/PiiRedactionService.cs` | Regex-based PII scrubber (AIR-S01) |
| CREATE | `Server/PropelIQ.Clinical/AI/Models/DocumentChunk.cs` | DTO: Text, PageNumber, StartTokenIndex, EndTokenIndex, DocumentId |
| CREATE | `Server/PropelIQ.Clinical/AI/Models/ChunkWithEmbedding.cs` | DTO: DocumentChunk + float[] Vector |

## External References

- [PdfPig — PDF text extraction .NET](https://github.com/UglyToad/PdfPig)
- [Tesseract OCR .NET wrapper](https://github.com/charlesw/tesseract)
- [OpenAI text-embedding-3-small model docs](https://platform.openai.com/docs/guides/embeddings)
- [Microsoft Semantic Kernel — ITextEmbeddingGenerationService](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/embeddings)
- [TiktokenSharp — token counting .NET](https://github.com/aiqinxuancai/TiktokenSharp)
- [AIR-R01 — 512-token chunking with 10% overlap](../docs/design.md)
- [AIR-S01 — PII redaction before external API transmission](../docs/design.md)

## Build Commands

```bash
cd Server

# Add NuGet packages
dotnet add PropelIQ.Clinical package PdfPig
dotnet add PropelIQ.Clinical package Tesseract
dotnet add PropelIQ.Clinical package TiktokenSharp
dotnet add PropelIQ.Clinical package Microsoft.SemanticKernel

# Restore & build
dotnet restore
dotnet build PropelIQ.sln
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Text-based PDF produces N chunks with each chunk ≤ 512 tokens (verified by token counter)
- [ ] Adjacent chunks share ≈51 token overlap
- [ ] Image-only PDF triggers OCR path (Tesseract)
- [ ] Image-only PDF with unreadable content throws `DocumentExtractionException`
- [ ] PII redaction replaces DOB, phone, SSN, email patterns with `[REDACTED]` before embedding call
- [ ] Embedding batch of 20 chunks returns 20 float[] vectors (1536 dimensions each)
- [ ] OpenAI API failure → exception logged with documentId, no PII; upstream handler catches and sets status=Failed
- [ ] AuditLog entry written after successful batch with chunkCount, tokenTotal, model (no patient identifiers)

## Implementation Checklist

- [ ] Implement `DocumentChunkingService` using PdfPig for text extraction; Tesseract OCR fallback for image-only PDFs (EC-1)
- [ ] Implement token-aware sliding window chunker: 512-token chunks, 51-token (10%) overlap, retain page number per chunk (AIR-R01)
- [ ] Implement `PiiRedactionService` — regex redaction of patient name, DOB, SSN, phone, email before any OpenAI API call (AIR-S01)
- [ ] Implement `EmbeddingGenerationService` via Semantic Kernel `ITextEmbeddingGenerationService` targeting `text-embedding-3-small`
- [ ] Batch embedding calls in groups of 20; apply 100ms inter-batch delay for rate limit compliance
- [ ] Catch and log embedding API failures (model, documentId, HTTP status) without PII in log entries (AIR-S03)
- [ ] Write AuditLog entry after successful embedding generation: chunkCount, tokenTotal, model — no PII (AIR-S03, NFR-009)
- [ ] Load OpenAI API key from `IConfiguration` — never hardcode (OWASP A02)
