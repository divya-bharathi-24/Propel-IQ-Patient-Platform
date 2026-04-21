import { type Locator, type Page } from '@playwright/test';
import path from 'path';

/** Minimal valid 1-byte-body PDF for test upload fixtures. */
export const MINIMAL_PDF_BUFFER = Buffer.from(
  '%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj ' +
    '2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj ' +
    '3 0 obj<</Type/Page/MediaBox[0 0 612 792]>>endobj\n' +
    'xref\n0 4\n0000000000 65535 f\n' +
    '%%EOF',
);

export class DocumentUploadPage {
  constructor(private readonly page: Page) {}

  get fileInput(): Locator {
    return this.page.getByTestId('document-upload-input');
  }

  get uploadButton(): Locator {
    return this.page.getByRole('button', { name: 'Upload documents' });
  }

  get progressBar(): Locator {
    return this.page.getByRole('progressbar');
  }

  get fileList(): Locator {
    return this.page.getByTestId('upload-file-list');
  }

  get successBanner(): Locator {
    return this.page.getByTestId('upload-success-banner');
  }

  get documentHistory(): Locator {
    return this.page.getByTestId('document-history-list');
  }

  fileError(fileName: string): Locator {
    const baseName = path.basename(fileName, path.extname(fileName));
    return this.page.getByTestId(`file-error-${baseName}`);
  }

  async uploadBufferedFiles(
    files: Array<{ name: string; mimeType: string; buffer: Buffer }>,
  ): Promise<void> {
    await this.fileInput.setInputFiles(files);
    await this.uploadButton.click();
  }

  async uploadPdfs(fileNames: string[]): Promise<void> {
    const files = fileNames.map((name) => ({
      name,
      mimeType: 'application/pdf',
      buffer: MINIMAL_PDF_BUFFER,
    }));
    await this.uploadBufferedFiles(files);
  }
}
