# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: e2e\patient_onboarding.spec.ts >> E2E Journey: Patient Onboarding (UC-001 → UC-002 → UC-007 → UC-008) >> Full patient onboarding from registration to verified profile
- Location: e2e\patient_onboarding.spec.ts:136:7

# Error details

```
Error: page.waitForFunction: Target page, context or browser has been closed
```

# Test source

```ts
  341 |         if (!comp) throw new Error('DocumentUploadComponent instance not found');
  342 |         const store = comp['store'] as { validateFiles: (files: FileList) => void } | undefined;
  343 |         if (!store?.validateFiles) throw new Error('store.validateFiles not found on component');
  344 |         const dt = new DataTransfer();
  345 |         for (const name of fileNames) {
  346 |           dt.items.add(new File(['%PDF-1.4'], name, { type: 'application/pdf' }));
  347 |         }
  348 |         store.validateFiles(dt.files);
  349 |         // Force Angular CD to commit signal changes to the DOM immediately.
  350 |         ng.applyChanges(uploadEl);
  351 |       }, d.documents.map((doc) => doc.name));
  352 |       // Upload button only appears after files are validated (inside @if block).
  353 |       await uploader.uploadButton.waitFor({ state: 'visible', timeout: 10_000 });
  354 |       await uploader.uploadButton.click();
  355 |       // After upload button click, the mocked HTTP response runs outside NgZone.
  356 |       // Force Angular CD to update the DOM with upload results.
  357 |       await page.evaluate(() => {
  358 |         const ng = (window as unknown as { ng?: { applyChanges: (el: Element) => void } }).ng;
  359 |         const uploadEl = document.querySelector('app-document-upload');
  360 |         if (ng && uploadEl) ng.applyChanges(uploadEl);
  361 |       });
  362 |       // Verify upload results using the rendered file names (the "N documents
  363 |       // uploaded successfully" text lives in a nested @if whose signal dependency
  364 |       // is not re-evaluated in the outside-zone CD cycle; file name text is always
  365 |       // rendered once the outer @if block commits to the DOM).
  366 |       await expect(page.getByRole('heading', { name: 'Upload Results' })).toBeVisible({ timeout: 15_000 });
  367 |       for (const doc of d.documents) {
  368 |         await expect(page.getByText(doc.name).last()).toBeVisible({ timeout: 10_000 });
  369 |       }
  370 |     });
  371 | 
  372 |     await test.step('Phase 3: Verify documents appear in history', async () => {
  373 |       // mat-card-title renders as a <div>, not a semantic heading, so use getByText.
  374 |       await expect(page.getByText('Upload History').first()).toBeVisible({ timeout: 15_000 });
  375 |     });
  376 | 
  377 |     // Phase 4 ──────────────────────────────────────────────────────────────
  378 | 
  379 |     await test.step('Phase 4: Staff logs in to review patient 360 view', async () => {
  380 |       // Mock login for staff role
  381 |       await page.route('**/api/auth/login**', route =>
  382 |         route.fulfill({
  383 |           status: 200,
  384 |           json: {
  385 |             accessToken: 'mock-access-token-staff',
  386 |             refreshToken: 'mock-refresh-token-staff',
  387 |             expiresIn: 3600,
  388 |             userId: 'mock-staff-001',
  389 |             role: 'Staff',
  390 |             deviceId: 'mock-device-staff',
  391 |           },
  392 |         }),
  393 |       );
  394 |       // Also mock staff dashboard
  395 |       await page.route('**/api/staff/dashboard**', route =>
  396 |         route.fulfill({ status: 200, json: { patients: [], pendingAlerts: 0 } }),
  397 |       );
  398 |       await page.goto('/auth/login');
  399 |       const login = new LoginPage(page);
  400 |       await login.login(d.staff.email, d.staff.password);
  401 |       // Wait for redirect to staff area after login
  402 |       await expect(page).toHaveURL(/walkin|dashboard|staff/, { timeout: 15_000 });
  403 |     });
  404 | 
  405 |     await test.step('Phase 4: Staff opens patient 360-degree view', async () => {
  406 |       await mockProfileVerifyBlocked(page);
  407 |       // Auth is in-memory only (never written to localStorage), so page.goto() would
  408 |       // trigger a full reload, destroy auth state, and cause authGuard to redirect to login.
  409 |       // Instead, trigger an in-app Angular Router navigation from the walkin component's
  410 |       // injected Router service — this preserves the in-memory auth state.
  411 |       await page.evaluate((patientId: string) => {
  412 |         const ng = (window as unknown as { ng?: { getComponent: (el: Element) => Record<string, unknown> | null } }).ng;
  413 |         if (!ng) throw new Error('Angular debug utilities not available (app must run in dev mode)');
  414 |         const walkinEl = document.querySelector('app-walkin-booking');
  415 |         if (!walkinEl) throw new Error('app-walkin-booking element not found on page');
  416 |         const comp = ng.getComponent(walkinEl);
  417 |         if (!comp) throw new Error('WalkInBookingComponent instance not found');
  418 |         const router = comp['router'] as { navigateByUrl: (path: string) => void } | undefined;
  419 |         if (!router) throw new Error('Router not found on WalkInBookingComponent');
  420 |         void router.navigateByUrl(`/staff/patients/${patientId}/360-view`);
  421 |       }, d.patientId);
  422 |       await expect(page).toHaveURL(new RegExp(`patients/${d.patientId}`), { timeout: 10_000 });
  423 |       const view = new ThreeSixtyViewPage(page);
  424 |       await expect(view.heading).toBeVisible({ timeout: 15_000 });
  425 |       // The 360-view store's HTTP response runs outside NgZone — force Angular CD
  426 |       // by passing the component INSTANCE (not DOM element) to ng.applyChanges.
  427 |       await page.evaluate(() => {
  428 |         const ng = (window as unknown as { ng?: { getComponent: (el: Element) => unknown; applyChanges: (comp: unknown) => void } }).ng;
  429 |         const el = document.querySelector('app-patient-360-view');
  430 |         if (!ng || !el) return;
  431 |         try {
  432 |           const comp = ng.getComponent(el);
  433 |           if (comp) ng.applyChanges(comp);
  434 |         } catch (_) { /* Component LView may still be initializing — proceed */ }
  435 |       });
  436 |     });
  437 | 
  438 |     await test.step('Phase 4: Staff sees medication conflict indicator', async () => {
  439 |       // Poll until the element appears, triggering Angular CD each iteration because
  440 |       // the HTTP mock response runs outside NgZone and signals may not auto-schedule.
> 441 |       await page.waitForFunction((field: string) => {
      |                  ^ Error: page.waitForFunction: Target page, context or browser has been closed
  442 |         const ng = (window as { ng?: { getComponent: (el: Element) => unknown; applyChanges: (c: unknown) => void } }).ng;
  443 |         const el = document.querySelector(`[data-testid="conflict-indicator-${field}"]`);
  444 |         if (el) return true;
  445 |         // Nudge Angular CD via the component instance
  446 |         const viewEl = document.querySelector('app-patient-360-view');
  447 |         if (ng && viewEl) {
  448 |           try { const c = ng.getComponent(viewEl); if (c) ng.applyChanges(c); } catch (_) {}
  449 |         }
  450 |         return false;
  451 |       }, d.conflict.field, { timeout: 15_000 });
  452 |       const view = new ThreeSixtyViewPage(page);
  453 |       await expect(view.conflictIndicator(d.conflict.field)).toBeVisible();
  454 |     });
  455 | 
  456 |     await test.step('Phase 4: Verification blocked until conflict resolved', async () => {
  457 |       const view = new ThreeSixtyViewPage(page);
  458 |       await view.verifyProfile();
  459 |       await expect(view.errorAlert).toContainText('Resolve all conflicts before verifying');
  460 |     });
  461 | 
  462 |     await test.step('Phase 4: Staff opens conflict and views both source values', async () => {
  463 |       const view = new ThreeSixtyViewPage(page);
  464 |       await view.conflictIndicator(d.conflict.field).click();
  465 |       await expect(view.conflictValue(1)).toContainText(d.conflict.value1);
  466 |       await expect(view.conflictValue(2)).toContainText(d.conflict.value2);
  467 |     });
  468 | 
  469 |     await test.step('Phase 4: Staff selects authoritative value', async () => {
  470 |       const view = new ThreeSixtyViewPage(page);
  471 |       await view.selectConflictValueButton(d.conflict.authoritativeValue).click();
  472 |       await expect(view.conflictIndicator(d.conflict.field)).toBeHidden();
  473 |     });
  474 | 
  475 |     await test.step('Phase 4: Staff verifies profile successfully', async () => {
  476 |       const view = new ThreeSixtyViewPage(page);
  477 |       await mockProfileVerifySuccess(page);
  478 |       await view.verifyProfile();
  479 |       await expect(view.profileStatusBadge).toContainText('Verified');
  480 |     });
  481 | 
  482 |     await test.step('Phase 4: Verified profile displays intake medications and allergies', async () => {
  483 |       await expect(page.getByTestId('intake-data-medications')).toContainText(d.conflict.value1);
  484 |       await expect(page.getByTestId('intake-data-allergies')).toContainText(d.intake.expectedAllergies[0]);
  485 |     });
  486 |   });
  487 | });
  488 | 
```