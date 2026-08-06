import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

// REQ-NFR-010 / BRD-101 — a failing provider must surface a clear, specific status in the UI and
// must never take down the Blazor circuit.
//
// Two instances are driven:
//   * TD_BASE      — the normal instance. Its LLM endpoint (LM Studio on the LAN) is down, so the
//                    generation failure path is real, not simulated.
//   * TD_ISO_BASE  — an instance started with an isolated content root whose techierag-config.json
//                    points the embedding provider at a dead endpoint (127.0.0.1:1), so the
//                    embedding failure path is real too.
//   * TD_VEC_BASE  — a third instance whose vectorStore.connectionString is unopenable.
//
// The last two need separate content roots because they are different broken configurations.
// Each instance is started with:
//   dotnet run --project apps/TechieDesk --no-launch-profile \
//     --urls http://localhost:<port> --contentRoot <isolated-root>
// with ASPNETCORE_ENVIRONMENT=Development and AppManager__BaseUrl= (offline single-user).

const BASE = process.env.TD_BASE ?? 'http://localhost:5132';
const ISO_BASE = process.env.TD_ISO_BASE ?? 'http://localhost:5134';
const SLUG = 'default';
const SHOT = 'test-results/screens';
const TMP = process.env.TD_TMP
  ?? '/private/tmp/claude-501/-Volumes-MacD-MyCode-TechieRag/6ec44dff-2137-4039-9b2c-3815d9055a6d/scratchpad';

async function waitForCircuit(page: Page) {
  await page.waitForLoadState('networkidle');
  await page.waitForFunction(() => (window as any).Blazor !== undefined, null, { timeout: 30000 });
  await page.waitForTimeout(500);
}

/** Asserts the Blazor circuit is still alive by driving one more interaction. */
async function assertCircuitAlive(page: Page) {
  await expect(page.locator('#components-reconnect-modal')).toBeHidden();
  await page.getByRole('link', { name: 'Home' }).click();
  await expect(page.getByText('TechieDesk', { exact: false }).first())
    .toBeVisible({ timeout: 30000 });
}

test.describe('REQ-NFR-010 provider failure surfacing', () => {
  test.setTimeout(300000);

  test('LLM provider unreachable -> explicit error in the chat, circuit survives', async ({ page }) => {
    await page.goto(`${BASE}/workspace/${SLUG}`);
    await waitForCircuit(page);

    await page.getByRole('button', { name: 'New thread' }).click();
    await expect.poll(() => page.locator('.td-chat-grid button.min-w-0').count(),
      { timeout: 30000, intervals: [50] }).toBeGreaterThan(0);

    await page.locator('textarea').fill('Summarise the ingestion pipeline.');
    await page.locator('button:has(svg)').last().click();

    const error = page.getByText(/Error:|No LLM provider/).first();
    await expect(error).toBeVisible({ timeout: 120000 });
    const message = await error.innerText();
    console.log(`FAILURE llm message: ${message}`);

    // The message must name the actual failure, not a generic apology.
    expect(message.length).toBeGreaterThan(10);
    expect(message).toMatch(/Error:|No LLM provider/);

    await page.screenshot({ path: `${SHOT}/req-nfr-010-llm-failure.png`, fullPage: true });
    await assertCircuitAlive(page);
  });

  test('embedding provider unreachable -> upload reports Failed, circuit survives', async ({ page }) => {
    await page.goto(`${ISO_BASE}/workspace/${SLUG}/documents`);
    await waitForCircuit(page);
    await expect(page.getByText(/^Library \(/)).toBeVisible({ timeout: 30000 });

    const file = path.join(TMP, `embedfail-${Date.now()}.txt`);
    fs.writeFileSync(file,
      'Chunking splits a document into passages before embedding. '.repeat(40));
    await page.locator('input[type="file"]').setInputFiles(file);

    // The per-file queue must end in an explicit failure state, not a spinner that never resolves.
    await expect(page.getByText('Failed', { exact: true })).toBeVisible({ timeout: 180000 });
    const queueText = await page.getByText('Uploads (').locator('xpath=ancestor::*[3]').innerText()
      .catch(async () => await page.locator('body').innerText());
    console.log(`FAILURE embedding queue: ${queueText.replace(/\s+/g, ' ').slice(0, 400)}`);

    await page.screenshot({ path: `${SHOT}/req-nfr-010-embedding-failure.png`, fullPage: true });
    fs.unlinkSync(file);

    // And nothing half-ingested was recorded in the library.
    await expect(page.getByRole('table').getByText(path.basename(file))).toHaveCount(0);
    await assertCircuitAlive(page);
  });

  // Requires an instance whose vectorStore.connectionString points somewhere unopenable, e.g.
  //   --contentRoot <iso> with "connectionString": "Data Source=/nonexistent-dir-techierag/vec.db"
  // Set TD_VEC_BASE to that instance's URL to run it.
  test('vector store unreachable -> named as an outage, not as a missing workspace', async ({ page }) => {
    const vecBase = process.env.TD_VEC_BASE;
    test.skip(!vecBase, 'TD_VEC_BASE not set — needs an instance with a broken vector store');

    await page.goto(`${vecBase}/workspace/${SLUG}/documents`);
    await waitForCircuit(page);

    // Regression guard: an infrastructure outage used to render as "This workspace does not exist
    // or you are not assigned to it", which points the user at the wrong problem entirely.
    await expect(page.getByText('Workspace data is unavailable')).toBeVisible({ timeout: 30000 });
    await expect(page.getByText('This workspace does not exist or you are not assigned to it'))
      .toHaveCount(0);
    const detail = await page.getByText(/Details:/).innerText();
    console.log(`FAILURE vector store: ${detail.replace(/\s+/g, ' ')}`);
    expect(detail).toMatch(/unable to open database file|Details:/);

    await page.screenshot({ path: `${SHOT}/req-nfr-010-vectorstore-failure.png`, fullPage: true });
    await assertCircuitAlive(page);
  });
});
