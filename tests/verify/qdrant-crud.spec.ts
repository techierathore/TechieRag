/**
 * Verification spec for the Qdrant Admin page (/qdrant-admin).
 *
 * Covers:
 *   REQ-UI-011 — Docker status detection + connection status/version cards
 *   REQ-UI-012 — Collection create / list / delete with cluster info
 *   REQ-UI-013 — Paginated vector browse + vector detail dialog
 *
 * Live infrastructure (owner-provisioned, DO NOT lifecycle it from tests):
 *   Qdrant v1.15.5 in Docker — REST http://127.0.0.1:60941, gRPC 127.0.0.1:60942,
 *   api-key auth enabled. Pre-existing collections (NEVER deleted/modified here):
 *   techierag_documents (13 pts), data-chatappex-documents (2 pts),
 *   techierag_chunks (1,043 pts, dim 1024), data-chatappex-chunks (69 pts).
 *
 * Design notes:
 *   - One shared browser page for the whole serial suite: QdrantAdminService is a
 *     singleton, but every FRESH Blazor circuit's initial RefreshStatusAsync
 *     re-configures the endpoint from the detected running container WITHOUT the
 *     API key, dropping the authenticated connection. Staying on one circuit
 *     keeps the Connect state alive across tests.
 *   - Blazor Server: every interaction is a SignalR round-trip, so waits are
 *     explicit and timeouts generous.
 *   - NOT EXERCISED (destructive against real owner data / infrastructure):
 *       * container Create/Start/Stop buttons (REQ-UI-011) — pre-existing container
 *       * per-vector Delete and any bulk delete (REQ-UI-013) — would remove real
 *         vectors from techierag_chunks
 */

import { test, expect, request } from '@playwright/test';
import type { BrowserContext, Page } from '@playwright/test';
import * as fs from 'fs';

const SCREEN_DIR = 'test-results/screens';
const QDRANT_HOST = '127.0.0.1';
const QDRANT_GRPC_PORT = '60942';
const QDRANT_REST = 'http://127.0.0.1:60941';
const QDRANT_API_KEY = 'V45x5j1V36zW19kNYF4Hkc';
const TMP_COLLECTION = 'verify_crud_tmp';
const POINTS_COLLECTION = 'techierag_chunks'; // verified via REST: 1,043 points, dim 1024

test.describe.serial('Qdrant Admin CRUD (/qdrant-admin)', () => {
  let context: BrowserContext;
  let page: Page;

  /** Collections DataTable: the only table with a "Points" column header. */
  const collectionsTable = () =>
    page.locator('table').filter({ has: page.locator('th', { hasText: 'Points' }) });

  /** Vectors DataTable inside the collection detail card ("Chunk Preview" header is unique). */
  const vectorsTable = () =>
    page.locator('table').filter({ has: page.locator('th', { hasText: 'Chunk Preview' }) });

  test.beforeAll(async ({ browser }) => {
    fs.mkdirSync(SCREEN_DIR, { recursive: true });

    // Hygiene: if a previous aborted run left the temp collection behind, remove it
    // via REST so the create test starts clean. This touches ONLY verify_crud_tmp.
    const rc = await request.newContext({
      extraHTTPHeaders: { 'api-key': QDRANT_API_KEY },
    });
    await rc.delete(`${QDRANT_REST}/collections/${TMP_COLLECTION}`).catch(() => {});
    await rc.dispose();

    context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
    page = await context.newPage();
  });

  test.afterAll(async () => {
    await context?.close();
  });

  test('REQ-UI-011: Connect shows Docker Available, Qdrant Connected and real server version 1.15.5', async () => {
    test.setTimeout(180_000);

    await page.goto('/qdrant-admin');
    await page.waitForLoadState('networkidle');
    await page.waitForFunction(() => document.title.includes('Qdrant Admin'), null, {
      timeout: 30_000,
    });

    // Initial RefreshStatusAsync probes Docker and attempts an (unauthenticated,
    // failing) gRPC connect — wait for the form to be interactive first.
    const connectBtn = page.getByRole('button', { name: 'Connect' });
    await expect(connectBtn).toBeVisible({ timeout: 30_000 });

    await page.getByPlaceholder('localhost').fill(QDRANT_HOST);
    await page.getByPlaceholder('6334').fill(QDRANT_GRPC_PORT);
    await page.getByPlaceholder('Enter API key...').fill(QDRANT_API_KEY);
    // Blur the last input so Blazor's @bind (change event) flushes before the click.
    await page.keyboard.press('Tab');

    await connectBtn.click();

    // Qdrant status card flips to "Connected" (a success toast also titled
    // "Connected" may coexist briefly — hence .first()).
    await expect(page.getByText('Connected', { exact: true }).first()).toBeVisible({
      timeout: 60_000,
    });

    // Docker card: "Available" (exact match cannot collide with "Not Available").
    await expect(page.getByText('Available', { exact: true })).toBeVisible({ timeout: 30_000 });

    // Version card must show the REAL server version — not "N/A", not a fabricated 1.12.x.
    await expect(page.getByText('1.15.5', { exact: true })).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText(/1\.12\./)).toHaveCount(0);

    // Container lifecycle buttons (Create Container / Start / Stop) are deliberately
    // NOT clicked: the running container is pre-existing owner infrastructure.

    await page.screenshot({
      path: `${SCREEN_DIR}/qdrant-connected-desktop.png`,
      fullPage: true,
    });
  });

  test('REQ-UI-012: collections table lists techierag_chunks with populated cells', async () => {
    test.setTimeout(120_000);

    const table = collectionsTable();
    await expect(table).toBeVisible({ timeout: 30_000 });

    // Render gate: at least one data row exists.
    const rowCount = await table.locator('tbody tr').count();
    expect(rowCount).toBeGreaterThan(0);

    const row = table.getByRole('row', { name: new RegExp(POINTS_COLLECTION) });
    await expect(row).toBeVisible({ timeout: 30_000 });

    // Name / Points / Vectors cells must all be non-empty (no blank render).
    const cells = row.getByRole('cell');
    expect(await cells.count()).toBeGreaterThanOrEqual(3);
    for (let i = 0; i < 3; i++) {
      const text = (await cells.nth(i).innerText()).trim();
      expect(text.length, `cell ${i} of ${POINTS_COLLECTION} row should be non-empty`).toBeGreaterThan(0);
    }

    await page.screenshot({
      path: `${SCREEN_DIR}/qdrant-collections-desktop.png`,
      fullPage: true,
    });
  });

  test('REQ-UI-012: create collection verify_crud_tmp via New Collection dialog', async () => {
    test.setTimeout(120_000);

    await page.getByRole('button', { name: 'New Collection' }).click();

    const dialog = page.getByRole('dialog').filter({ hasText: 'Create New Collection' });
    await expect(dialog).toBeVisible({ timeout: 15_000 });

    await dialog.getByPlaceholder('my_collection').fill(TMP_COLLECTION);
    await page.keyboard.press('Tab');
    // Vector Dimensions defaults to 1024 and Distance to Cosine — left as-is.

    const createBtn = dialog.getByRole('button', { name: 'Create', exact: true });
    await expect(createBtn).toBeEnabled({ timeout: 15_000 });
    await createBtn.click();

    // Page refreshes the collection list after creation.
    await expect(
      collectionsTable().getByRole('row', { name: new RegExp(TMP_COLLECTION) })
    ).toBeVisible({ timeout: 30_000 });
  });

  test('REQ-UI-012: delete collection verify_crud_tmp and confirm removal', async () => {
    test.setTimeout(120_000);

    const tmpRow = collectionsTable().getByRole('row', { name: new RegExp(TMP_COLLECTION) });
    await expect(tmpRow).toBeVisible({ timeout: 30_000 });

    // SAFETY: the Delete button is resolved strictly INSIDE the verify_crud_tmp row —
    // no other collection's Delete can be hit.
    await tmpRow.getByRole('button', { name: 'Delete', exact: true }).click();

    await expect(
      collectionsTable().getByRole('row', { name: new RegExp(TMP_COLLECTION) })
    ).toHaveCount(0, { timeout: 30_000 });

    // Pre-existing collections remain untouched.
    await expect(
      collectionsTable().getByRole('row', { name: new RegExp(POINTS_COLLECTION) })
    ).toBeVisible();
  });

  test('REQ-UI-013: browse techierag_chunks vectors — rows render, pager pages Next/Previous', async () => {
    test.setTimeout(120_000);

    await collectionsTable()
      .getByRole('row', { name: new RegExp(POINTS_COLLECTION) })
      .getByRole('button', { name: 'Browse', exact: true })
      .click();

    // Collection detail card opens with stats.
    await expect(page.getByText(`Collection: ${POINTS_COLLECTION}`)).toBeVisible({
      timeout: 30_000,
    });

    const table = vectorsTable();
    await expect(table).toBeVisible({ timeout: 30_000 });

    // Rows render with non-empty ID cells (first column is the truncated point id).
    const rows = table.locator('tbody tr');
    const n = await rows.count();
    expect(n).toBeGreaterThan(0);
    for (let i = 0; i < Math.min(n, 5); i++) {
      const id = (await rows.nth(i).getByRole('cell').first().innerText()).trim();
      expect(id.length, `vector row ${i} ID cell should be non-empty`).toBeGreaterThan(0);
    }

    // techierag_chunks has 1,043 points (verified via REST) — pager must be active.
    await expect(page.getByText(/Showing\s+1\s*.\s*20\s+of\s+[\d,]+\s+vectors/)).toBeVisible({
      timeout: 30_000,
    });

    await page.screenshot({
      path: `${SCREEN_DIR}/qdrant-vectors-desktop.png`,
      fullPage: true,
    });

    await page.getByRole('button', { name: 'Next', exact: true }).click();
    await expect(page.getByText(/Showing\s+21\s*.\s*40\s+of\s+[\d,]+\s+vectors/)).toBeVisible({
      timeout: 30_000,
    });

    await page.getByRole('button', { name: 'Previous', exact: true }).click();
    await expect(page.getByText(/Showing\s+1\s*.\s*20\s+of\s+[\d,]+\s+vectors/)).toBeVisible({
      timeout: 30_000,
    });
  });

  test('REQ-UI-013: vector detail dialog shows non-empty ID and payload, then closes', async () => {
    test.setTimeout(120_000);

    await vectorsTable()
      .locator('tbody tr')
      .first()
      .getByRole('button', { name: 'View', exact: true })
      .click();

    const dialog = page.getByRole('dialog').filter({ hasText: 'Vector Details' });
    await expect(dialog).toBeVisible({ timeout: 30_000 });

    // Full (untruncated) point id renders in the monospace ID field.
    const idValue = (await dialog.locator('div.font-mono').first().innerText()).trim();
    expect(idValue.length, 'vector detail ID should be a full point id').toBeGreaterThan(8);

    // Payload <pre> must be non-empty (techierag_chunks points carry Text/Metadata payload).
    const payloadText = (await dialog.locator('pre').innerText()).trim();
    expect(payloadText.length, 'vector payload should be non-empty').toBeGreaterThan(0);

    // Close via the dialog's Close button. The dialog's Delete button is NEVER
    // clicked — it would remove a real vector from owner data. Likewise, bulk
    // delete is intentionally NOT exercised in this suite (REQ-UI-013 partial).
    await dialog.getByRole('button', { name: 'Close' }).first().click();
    await expect(dialog).toBeHidden({ timeout: 15_000 });
  });

  test('REQ-UI-011: mobile /qdrant-admin (390x844) renders without horizontal overflow', async ({
    browser,
  }) => {
    test.setTimeout(120_000);

    const mobileContext = await browser.newContext({
      viewport: { width: 390, height: 844 },
    });
    const mobilePage = await mobileContext.newPage();

    await mobilePage.goto('/qdrant-admin');
    await mobilePage.waitForLoadState('networkidle');
    await mobilePage.waitForFunction(() => document.title.includes('Qdrant Admin'), null, {
      timeout: 30_000,
    });
    // Let the initial status refresh (Docker probe + connection test) settle so the
    // widest content (status cards, containers table) has rendered.
    await mobilePage.waitForTimeout(3_000);

    const scrollWidth = await mobilePage.evaluate(
      () => document.documentElement.scrollWidth
    );
    expect(scrollWidth, 'mobile overflow gate: scrollWidth must fit 390px viewport').toBeLessThanOrEqual(395);

    await mobilePage.screenshot({
      path: `${SCREEN_DIR}/qdrant-admin-mobile.png`,
      fullPage: true,
    });

    await mobileContext.close();
  });
});
