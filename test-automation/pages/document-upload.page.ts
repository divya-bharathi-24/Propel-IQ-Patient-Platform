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
    return this.page.getByRole('button', { name: 'Upload selected PDF files' });
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
    // The file input uses .visually-hidden (clip: rect(0,0,0,0), 1×1 px) which
    // causes Playwright ≥1.38 visibility checks on setInputFiles to fail.
    // Temporarily expose it, set files (Playwright dispatches the change event),
    // then wait for the upload button that renders only after files are selected.
    await this.fileInput.evaluate((el: HTMLElement) => {
      el.style.clip = 'auto';
      el.style.overflow = 'visible';
      el.style.width = '1px';
      el.style.height = '1px';
    });
    await this.fileInput.setInputFiles(files);
    await this.uploadButton.waitFor({ state: 'visible', timeout: 10_000 });
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
