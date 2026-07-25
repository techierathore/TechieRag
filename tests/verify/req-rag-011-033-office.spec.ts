// REQ-RAG-033 (BRD-114) + REQ-RAG-011 (BRD-41) smoke.
//
// Uploads a real XLSX, PPTX and CSV through the TechieDesk document library UI and asserts
// RENDER-TRUTH (each lands with a non-zero chunk count and its row renders in the library
// table) plus VISUAL-TRUTH (no horizontal overflow / clipping at 1280 and 390 wide).
//
// Boot: dotnet run --project apps/TechieDesk --no-launch-profile --urls http://localhost:5111
// with AppManager__BaseUrl empty (offline single-user Admin).
import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

const BASE = process.env.SMOKE_BASE ?? 'http://localhost:5111';
const FIXTURES = process.env.SMOKE_FIXTURES ?? '';
const OUT = 'test-results/req-rag-033';
fs.mkdirSync(OUT, { recursive: true });

const DOCS_PATH = '/workspace/default/documents';

const UPLOADS = [
  { file: 'smoke-budget.xlsx', row: 'smoke-budget', type: 'XLSX' },
  { file: 'smoke-kickoff.pptx', row: 'smoke-kickoff', type: 'PPTX' },
  { file: 'smoke-people.csv', row: 'smoke-people', type: 'CSV' },
];

/** Navigate to the document library and wait for the Blazor circuit to render it. */
async function gotoLibrary(page: Page): Promise<void> {
  await page.goto(BASE + DOCS_PATH, { waitUntil: 'networkidle', timeout: 60000 });
  await expect(page.getByText(/^Library \(/)).toBeVisible({ timeout: 60000 });
}

test.describe.serial('REQ-RAG-033 + REQ-RAG-011 office formats', () => {
  test('uploads XLSX, PPTX and CSV and renders non-zero chunk counts', async ({ page }) => {
    test.setTimeout(300000);
    page.setViewportSize({ width: 1280, height: 900 });

    await gotoLibrary(page);

    const paths = UPLOADS.map((u) => path.join(FIXTURES, u.file));
    for (const p of paths) {
      expect(fs.existsSync(p), `fixture missing: ${p}`).toBe(true);
    }

    // Drive the hidden file input behind the drag-drop FileUpload component.
    await page.locator('input[type="file"]').first().setInputFiles(paths);

    // Each file must reach a terminal success state — never "Rejected".
    await expect(page.getByText('Rejected')).toHaveCount(0, { timeout: 20000 });
    await expect(page.getByText(/Embedding…/)).toHaveCount(0, { timeout: 240000 });

    await page.waitForTimeout(2000);
    await gotoLibrary(page);

    // RENDER-TRUTH: the table must actually carry rows, each with a non-zero chunk count.
    const table = page.locator('table').first();
    await expect(table).toBeVisible({ timeout: 30000 });

    for (const upload of UPLOADS) {
      const row = table.locator('tr', { hasText: upload.row }).first();
      await expect(row, `${upload.type} row missing from library`).toBeVisible({ timeout: 30000 });

      const cells = await row.locator('td').allInnerTexts();
      const chunkValues = cells
        .map((c) => c.trim())
        .filter((c) => /^\d+$/.test(c))
        .map((c) => parseInt(c, 10));

      const maxChunks = chunkValues.length ? Math.max(...chunkValues) : 0;
      expect(maxChunks, `${upload.type} ingested with zero chunks: ${JSON.stringify(cells)}`).toBeGreaterThan(0);
      console.log(`${upload.type} (${upload.row}) chunk cells=${JSON.stringify(chunkValues)}`);
    }

    await page.screenshot({ path: `${OUT}/library-desktop-1280.png`, fullPage: true });
  });

  test('library renders without overflow or clipping at 1280 and 390', async ({ page }) => {
    test.setTimeout(120000);

    for (const vp of [
      { w: 1280, h: 900, tag: 'desktop-1280' },
      { w: 390, h: 844, tag: 'mobile-390' },
    ]) {
      await page.setViewportSize({ width: vp.w, height: vp.h });
      const consoleErrors: string[] = [];
      page.on('pageerror', (e) => consoleErrors.push(e.message));

      await gotoLibrary(page);
      await page.waitForTimeout(1200);
      await page.screenshot({ path: `${OUT}/library-${vp.tag}.png`, fullPage: true });

      // VISUAL-TRUTH: the page body must never scroll horizontally.
      const overflow = await page.evaluate(
        () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
      );
      expect(overflow, `horizontal page overflow at ${vp.w}px`).toBeLessThanOrEqual(1);

      // Nothing may sit off the left edge or beyond the viewport's right edge.
      const strays = await page.evaluate((width) => {
        const bad: string[] = [];
        document.querySelectorAll('body *').forEach((el) => {
          const r = el.getBoundingClientRect();
          if (r.width === 0 || r.height === 0) return;
          // Elements inside an overflow-x:auto wrapper are allowed to extend past it.
          if (el.closest('[style*="overflow-x:auto"], .overflow-x-auto')) return;
          if (r.left < -1 || r.right > width + 1) {
            bad.push(`${el.tagName}.${(el.className || '').toString().slice(0, 40)} [${Math.round(r.left)}..${Math.round(r.right)}]`);
          }
        });
        return bad.slice(0, 8);
      }, vp.w);

      expect(strays, `off-viewport elements at ${vp.w}px`).toEqual([]);
      expect(consoleErrors, `JS errors at ${vp.w}px`).toEqual([]);
      console.log(`${vp.tag}: overflow=${overflow}px, strays=0`);
    }
  });

  // REQ-RAG-011: the rejection half of the acceptance must keep working now that XLSX/PPTX
  // are accepted — a format with no processor still gets a clear, friendly per-file message.
  test('still rejects a genuinely unsupported type with a clear message', async ({ page }) => {
    test.setTimeout(120000);
    page.setViewportSize({ width: 1280, height: 900 });
    await gotoLibrary(page);

    const pngPath = path.join(FIXTURES, 'smoke-not-a-doc.png');
    // A 1x1 PNG — a real binary image the library has no processor for.
    fs.writeFileSync(
      pngPath,
      Buffer.from(
        'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
        'base64',
      ),
    );

    await page.locator('input[type="file"]').first().setInputFiles([pngPath]);

    // The accept filter is the first line of defence and rejects it by name, in plain language.
    // (UploadTypePolicy remains the server-side backstop — see TechieDesk.Tests
    // UploadTypePolicyTests, which covers the full accept/reject matrix.)
    await expect(
      page.getByText(/smoke-not-a-doc\.png is not an accepted file type/),
    ).toBeVisible({ timeout: 30000 });

    // It must never silently land in the library.
    await expect(page.locator('table').first().locator('tr', { hasText: 'smoke-not-a-doc' })).toHaveCount(0);

    // The old "coming in a later release" copy must be gone for good.
    await expect(page.getByText(/later release/i)).toHaveCount(0);

    await page.screenshot({ path: `${OUT}/rejection-desktop-1280.png`, fullPage: true });
  });

  // Leave the shared workspace as we found it — remove only the rows this spec created.
  test('cleans up the documents this spec ingested', async ({ page }) => {
    test.setTimeout(180000);
    page.setViewportSize({ width: 1280, height: 900 });

    for (const upload of UPLOADS) {
      await gotoLibrary(page);
      const table = page.locator('table').first();
      const row = table.locator('tr', { hasText: upload.row }).first();
      if ((await row.count()) === 0) continue;

      await row.getByRole('button', { name: 'Delete', exact: true }).click();
      // Confirm in the delete dialog (its button reads "Delete everywhere").
      await page.getByRole('button', { name: 'Delete everywhere' }).click();
      await page.waitForTimeout(2000);
    }

    await gotoLibrary(page);
    for (const upload of UPLOADS) {
      await expect(page.locator('table').first().locator('tr', { hasText: upload.row })).toHaveCount(0, {
        timeout: 15000,
      });
    }
  });
});
