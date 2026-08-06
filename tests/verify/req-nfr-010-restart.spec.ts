import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

// REQ-NFR-010 / BRD-101 — "restart loses no persisted data".
//
// Phase A creates real state through the UI (workspace, tuned settings, ingested + pinned
// document, renamed thread, a chat turn). The app is then restarted out-of-band. Phase B
// re-opens the app and proves every piece of that state came back.
//
// Run as:
//   npx playwright test tests/verify/req-nfr-010-restart.spec.ts -g "phase A"
//   <restart the app>
//   npx playwright test tests/verify/req-nfr-010-restart.spec.ts -g "phase B"

const BASE = process.env.TD_BASE ?? 'http://localhost:5132';
const SHOT = 'test-results/screens';
// Playwright wipes test-results/ at the start of every run, so the phase-A manifest must live
// outside it to survive the restart between the two phases.
const MANIFEST = process.env.TD_MANIFEST
  ?? '/private/tmp/claude-501/-Volumes-MacD-MyCode-TechieRag/6ec44dff-2137-4039-9b2c-3815d9055a6d/scratchpad/req-nfr-010-manifest.json';
const PDF = process.env.TD_PDF
  ?? '/private/tmp/claude-501/-Volumes-MacD-MyCode-TechieRag/6ec44dff-2137-4039-9b2c-3815d9055a6d/scratchpad/bench10.pdf';

interface Manifest {
  workspaceName: string;
  slug: string;
  systemPrompt: string;
  topK: string;
  threadTitle: string;
  question: string;
  documentName: string;
}

async function waitForCircuit(page: Page) {
  await page.waitForLoadState('networkidle');
  await page.waitForFunction(() => (window as any).Blazor !== undefined, null, { timeout: 30000 });
  await page.waitForTimeout(500);
}

test.describe('REQ-NFR-010 restart safety', () => {
  test.setTimeout(300000);

  test('phase A — create real state through the UI', async ({ page }) => {
    const stamp = Date.now();
    const manifest: Manifest = {
      workspaceName: `restartproof${stamp}`,
      slug: `restartproof${stamp}`,
      systemPrompt: `Persisted system prompt marker ${stamp}.`,
      topK: '7',
      threadTitle: `restart thread ${stamp}`,
      question: `Which chunk size does the pipeline use? marker ${stamp}`,
      documentName: `restartdoc${stamp}.pdf`,
    };

    // 1. Create a workspace.
    await page.goto(`${BASE}/`);
    await waitForCircuit(page);
    await page.getByRole('button', { name: 'New workspace' }).click();
    await page.getByPlaceholder('Workspace name').fill(manifest.workspaceName);
    await page.getByRole('button', { name: 'Create', exact: true }).click();
    await expect(page.getByRole('link', { name: manifest.workspaceName }))
      .toBeVisible({ timeout: 30000 });

    // 2. Tune its settings — system prompt + Top-K.
    await page.goto(`${BASE}/workspace/${manifest.slug}/settings`);
    await waitForCircuit(page);
    await page.getByPlaceholder('You are a helpful assistant…').fill(manifest.systemPrompt);
    // Top-K lives on the Retrieval tab, whose content is only rendered once the tab is selected.
    await page.getByText('Retrieval', { exact: true }).click();
    await page.locator('input[aria-label="Top-K snippets"]').fill(manifest.topK);
    await page.getByRole('button', { name: 'Save changes' }).click();
    await expect(page.getByText('Workspace settings updated.')).toBeVisible({ timeout: 30000 });

    // 3. Ingest a document into it, pinned.
    await page.goto(`${BASE}/workspace/${manifest.slug}/documents`);
    await waitForCircuit(page);
    await expect(page.getByText(/^Library \(/)).toBeVisible({ timeout: 30000 });
    await page.locator('#pin-on-upload').click();

    const unique = path.join(path.dirname(PDF), manifest.documentName);
    fs.writeFileSync(unique, Buffer.concat([
      fs.readFileSync(PDF), Buffer.from(`\n% restart-proof ${stamp}\n`),
    ]));
    await page.locator('input[type="file"]').setInputFiles(unique);
    await expect(page.getByRole('table').getByText(manifest.documentName))
      .toBeVisible({ timeout: 180000 });
    fs.unlinkSync(unique);
    await page.screenshot({ path: `${SHOT}/req-nfr-010-phaseA-docs.png`, fullPage: true });

    // 4. Create a thread, rename it, and send a question (the user turn is persisted before
    //    the LLM is contacted, so it must survive even though generation fails on this host).
    await page.goto(`${BASE}/workspace/${manifest.slug}`);
    await waitForCircuit(page);
    await page.getByRole('button', { name: 'New thread' }).click();
    await expect.poll(() => page.locator('.td-chat-grid button.min-w-0').count(),
      { timeout: 30000, intervals: [50] }).toBe(1);

    await page.locator('.td-chat-grid button').filter({ hasText: /Actions for thread|^$/ })
      .last().click();
    await page.getByText('Rename', { exact: true }).click();
    await page.getByPlaceholder('Thread title').fill(manifest.threadTitle);
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page.getByText(manifest.threadTitle).first()).toBeVisible({ timeout: 30000 });

    await page.locator('textarea').fill(manifest.question);
    await page.locator('button:has(svg)').last().click();
    await expect(page.getByText(manifest.question).first()).toBeVisible({ timeout: 30000 });
    // Let the send complete (it fails at generation — that is expected and is asserted below).
    await expect(page.getByText(/Error:|No LLM provider/)).toBeVisible({ timeout: 120000 });

    await page.screenshot({ path: `${SHOT}/req-nfr-010-phaseA-chat.png`, fullPage: true });

    fs.writeFileSync(MANIFEST, JSON.stringify(manifest, null, 2));
    console.log('RESTART phase A manifest', JSON.stringify(manifest));
  });

  test('phase B — every piece of that state survived the restart', async ({ page }) => {
    const manifest: Manifest = JSON.parse(fs.readFileSync(MANIFEST, 'utf8'));
    console.log('RESTART phase B verifying', JSON.stringify(manifest));

    // Workspace itself.
    await page.goto(`${BASE}/`);
    await waitForCircuit(page);
    await expect(page.getByRole('link', { name: manifest.workspaceName }))
      .toBeVisible({ timeout: 30000 });

    // Workspace settings.
    await page.goto(`${BASE}/workspace/${manifest.slug}/settings`);
    await waitForCircuit(page);
    await expect(page.getByPlaceholder('You are a helpful assistant…'))
      .toHaveValue(manifest.systemPrompt, { timeout: 30000 });
    await page.getByText('Retrieval', { exact: true }).click();
    await expect(page.locator('input[aria-label="Top-K snippets"]'))
      .toHaveValue(manifest.topK, { timeout: 30000 });
    await page.screenshot({ path: `${SHOT}/req-nfr-010-phaseB-settings.png`, fullPage: true });

    // Ingested document, still listed and still pinned.
    await page.goto(`${BASE}/workspace/${manifest.slug}/documents`);
    await waitForCircuit(page);
    const docRow = page.getByRole('row').filter({ hasText: manifest.documentName });
    await expect(docRow).toBeVisible({ timeout: 30000 });
    // The pin button's tooltip is the authoritative signal for pinned state.
    await expect(docRow.locator('button[title="Pinned — always in workspace context"]'))
      .toBeVisible({ timeout: 30000 });
    const rowText = await docRow.innerText();
    console.log(`RESTART document row after restart: ${rowText.replace(/\s+/g, ' ')}`);
    expect(rowText).toContain('Embedded');
    await page.screenshot({ path: `${SHOT}/req-nfr-010-phaseB-docs.png`, fullPage: true });

    // Thread with its renamed title, and the persisted user message.
    await page.goto(`${BASE}/workspace/${manifest.slug}`);
    await waitForCircuit(page);
    await expect(page.getByText(manifest.threadTitle).first()).toBeVisible({ timeout: 30000 });
    await expect(page.getByText(manifest.question).first()).toBeVisible({ timeout: 30000 });
    await page.screenshot({ path: `${SHOT}/req-nfr-010-phaseB-chat.png`, fullPage: true });
  });
});
