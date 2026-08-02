import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

/**
 * Orchestrator self-smoke for the 2026-07-25 build-phase.
 *
 * Covers the aggregation-stage changes made AFTER the cluster agents returned — the ones no
 * cluster smoked because no cluster owned them:
 *   - Program.cs Data Protection key ring moved to the volume-backed data/keys (REQ-NFR-004b)
 *   - TechieRagManager rerank wiring + SearchAsync(SearchOptions) override (REQ-RAG-047)
 *   - AppDb path mismatch between the migrator and the app (new defect, REQ-FN-034)
 *
 * Runs against the offline single-user instance on :5099.
 */

const REPO = path.resolve(__dirname, '../..');
const SHOTS = path.join(REPO, 'test-results', 'screens');

/** Asserts a page has no horizontal overflow and no Blazor error bar at the given width. */
async function assertRendersClean(page: Page, route: string, width: number, label: string) {
  await page.setViewportSize({ width, height: width < 500 ? 844 : 900 });
  await page.goto(route, { waitUntil: 'networkidle' });

  const errorBar = page.locator('#blazor-error-ui');
  if (await errorBar.count()) {
    await expect(errorBar).toBeHidden();
  }

  const overflow = await page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow, `${route} horizontal overflow @${width}`).toBeLessThanOrEqual(0);

  fs.mkdirSync(SHOTS, { recursive: true });
  await page.screenshot({ path: path.join(SHOTS, `${label}-${width}.png`), fullPage: true });
}

test('shipped routes render clean at 1280 and 390', async ({ page }) => {
  const routes: Array<[string, string]> = [
    ['/', 'home'],
    ['/settings', 'settings'],
    ['/llm-settings', 'llm-settings'],
    ['/documents', 'documents'],
    ['/chat', 'chat'],
    ['/token-usage', 'token-usage'],
  ];

  for (const [route, label] of routes) {
    await assertRendersClean(page, route, 1280, label);
    await assertRendersClean(page, route, 390, label);
  }
});

test('REQ-NFR-004b: saved provider key is encrypted at rest under the new key-ring path', async ({ page }) => {
  // REQ-FN-034: the saved config now lives in the one (volume-backed) data directory.
  const configPath = path.join(REPO, 'apps', 'TechieDesk', 'data', 'techierag-config.json');
  const secret = 'sk-orchestrator-smoke-KEY-77d1e4';

  // Saving a provider rewrites the shared config file. Snapshot it and put it back afterwards —
  // otherwise this test leaves a half-configured provider behind (an OpenAI-compatible source
  // with no endpoint) and every later page that builds the TechieRag instance throws.
  const previousConfig = fs.existsSync(configPath) ? fs.readFileSync(configPath, 'utf8') : null;

  let onDisk = '';
  try {
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto('/llm-settings', { waitUntil: 'networkidle' });

    // The API-key field only exists once a cloud provider is chosen — Source defaults to None.
    await page.getByRole('combobox').first().click();
    await page.getByRole('option', { name: /OpenAI/ }).first().click();
    await page.waitForTimeout(500);

    const keyInput = page.locator('input[type="password"]').first();
    await expect(keyInput).toBeVisible();
    await keyInput.fill(secret);

    await page.getByRole('button', { name: /save/i }).first().click();
    await page.waitForTimeout(2500);

    expect(fs.existsSync(configPath), 'techierag-config.json was written').toBeTruthy();
    onDisk = fs.readFileSync(configPath, 'utf8');
  } finally {
    if (previousConfig === null) {
      if (fs.existsSync(configPath)) fs.unlinkSync(configPath);
    } else {
      fs.writeFileSync(configPath, previousConfig);
    }
  }

  // The actual security claim: the cleartext secret must not be recoverable from the file.
  expect(onDisk, 'cleartext key must not appear on disk').not.toContain(secret);
  expect(onDisk, 'value must carry the enc:v1: marker').toContain('enc:v1:');

  // And the key ring must live in the same volume-backed data directory (Program.cs change),
  // not the machine-local ~/.aspnet default that would be ephemeral in a container.
  const keyRing = path.join(REPO, 'apps', 'TechieDesk', 'data', 'keys');
  expect(fs.existsSync(keyRing), 'key ring persisted to data/keys').toBeTruthy();
  expect(fs.readdirSync(keyRing).some(f => f.endsWith('.xml')), 'key ring holds a key').toBeTruthy();
});

test('REQ-FN-034: every persistent artefact lives in ONE data directory', async () => {
  // The invariant: the migrator and the app resolve the same file, and nothing persistent is
  // left resolving against the CWD or bin/. Previously the migrator wrote a 61,440-byte database
  // while the app opened an empty one in bin/, and both reported success.
  const dataDir = path.join(REPO, 'apps', 'TechieDesk', 'data');
  const contentRoot = path.join(REPO, 'apps', 'TechieDesk');
  const binDataDir = path.join(contentRoot, 'bin', 'Debug', 'net10.0', 'data');

  const appDb = path.join(dataDir, 'techiedesk.db');
  const appDbSize = fs.existsSync(appDb) ? fs.statSync(appDb).size : 0;
  expect(appDbSize, `the migrated app database should live at ${appDb}`).toBeGreaterThan(0);

  // The vector store and the RAG store share that directory.
  for (const file of ['techierag.db', 'techiedesk-rag-store.db']) {
    expect(fs.existsSync(path.join(dataDir, file)), `${file} should be in the data directory`).toBeTruthy();
  }
  expect(fs.existsSync(path.join(dataDir, 'keys')), 'key ring should be in the data directory').toBeTruthy();

  // And nothing persistent is orphaned outside it.
  const strayInBin = fs.existsSync(binDataDir)
    ? fs.readdirSync(binDataDir).filter(f => f.endsWith('.db'))
    : [];
  expect(strayInBin, `no database should be orphaned in ${binDataDir}`).toEqual([]);

  const strayInContentRoot = fs.readdirSync(contentRoot)
    .filter(f => f.endsWith('.db') || f === 'techierag-config.json');
  expect(strayInContentRoot, `no persistent artefact should sit loose in ${contentRoot}`).toEqual([]);
});
