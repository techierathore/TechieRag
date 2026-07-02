import { test, expect, Page } from '@playwright/test';

/**
 * RAG data-path verification against the LIVE sample app (http://localhost:5099).
 *
 * Live backends: SQLite-vec store (techieragex.db), embedded BGE-M3 ONNX embeddings,
 * LM Studio at http://192.168.1.13:1234 (qwen2.5-coder-32b-instruct).
 *
 * SERIAL: order matters. The LLM connection test + chat run BEFORE the token-usage
 * assertion, because the token tracker is in-memory per app session and this spec's
 * earlier tests generate the usage that REQ-UI-008 asserts on.
 *
 * SAFETY RULES honoured here:
 *  - never clicks Save / Reset on /llm-settings (they mutate live config)
 *  - never clicks "Clear All Data" or "Reset Session"
 *  - ingests exactly ONE doc named 'verify-datapath-tmp' and deletes ONLY that row afterwards
 */

const TMP_DOC = 'verify-datapath-tmp';
const MODEL = 'qwen2.5-coder-32b-instruct';

test.use({ viewport: { width: 1280, height: 800 } });

/** Navigate and wait for the Blazor Server circuit to render the page. */
async function gotoAndSettle(page: Page, path: string, titlePattern: RegExp): Promise<void> {
  await page.goto(path);
  await page.waitForLoadState('networkidle');
  await page.waitForFunction(
    (src) => new RegExp(src).test(document.title),
    titlePattern.source,
    { timeout: 30000 },
  );
}

/** Blazor @bind commits on the change event — blur the field after fill so the binding fires. */
async function fillAndCommit(page: Page, locator: ReturnType<Page['locator']>, value: string): Promise<void> {
  await locator.fill(value);
  await locator.blur();
}

/** The "Documents" count in the /text-ingestion Statistics sidebar. */
function textIngestionDocsStat(page: Page) {
  return page
    .locator('div.flex.justify-between')
    .filter({ hasText: /^Documents/ })
    .locator('span.font-medium');
}

/** The stats grid on /ingestion (Documents / Chunks / Storage / Last Ingestion). */
function ingestionStatsGrid(page: Page) {
  return page.locator('div.grid.grid-cols-2').filter({ hasText: 'Chunks' }).first();
}

test.describe.serial('RAG data path (live LM Studio + SQLite-vec)', () => {
  test('REQ-UI-009 LLM connection test succeeds against live LM Studio', async ({ page }) => {
    test.setTimeout(120000);
    await gotoAndSettle(page, '/llm-settings', /LLM Settings/);

    // Wait for the config to load (the page shows a spinner until then).
    const testButton = page.getByRole('button', { name: 'Test LLM Connection' });
    await expect(testButton).toBeVisible({ timeout: 30000 });

    // Click ONLY the connection test — never Save / Reset on this page.
    await testButton.click();

    // Success renders an inline Alert: "Connected - <model> via <provider> (response: Nms)"
    // (the page bounds the test at 20s internally, so 45s is generous).
    // The message renders in BOTH the inline Alert and the success toast — take the first.
    const result = page.getByText(/Connected - .+ \(response: \d+ms\)/).first();
    await expect(result).toBeVisible({ timeout: 45000 });
    await expect(result).toContainText(MODEL);
    await expect(page.getByText(/Connection failed|timed out/)).toHaveCount(0);
  });

  test('REQ-UI-004 ingest raw text, verify stats + document row, then targeted cleanup', async ({ page }) => {
    test.setTimeout(120000);

    // --- Ingest on /text-ingestion ---
    await gotoAndSettle(page, '/text-ingestion', /Text Ingestion - TechieRag/);

    // Capture the pre-ingest Documents count from the Statistics sidebar (expected 2).
    const docsStat = textIngestionDocsStat(page);
    await expect(docsStat).toBeVisible({ timeout: 30000 });
    const before = parseInt((await docsStat.innerText()).trim(), 10);
    expect(before).toBeGreaterThanOrEqual(1); // store already has data (2 docs / 151 chunks)

    await fillAndCommit(page, page.getByPlaceholder('My Document'), TMP_DOC);
    await fillAndCommit(
      page,
      page.getByPlaceholder('Paste your text content here...'),
      'TechieRag verification smoke document. This temporary document exists only to prove ' +
        'the ingest write path works end to end. It travels through chunking, BGE-M3 ONNX ' +
        'embedding, and the SQLite-vec vector store. It is deleted immediately after the check.',
    );

    await page.getByRole('button', { name: 'Ingest Text', exact: true }).click();

    // Success toast: "Ingested 'verify-datapath-tmp' (ID: <guid>)" — embedding can take a while.
    await expect(page.getByText(`Ingested '${TMP_DOC}'`)).toBeVisible({ timeout: 60000 });

    // Sidebar stats refresh in place after ingest.
    await expect(docsStat).toHaveText(String(before + 1), { timeout: 30000 });

    // --- Verify on /ingestion ---
    await gotoAndSettle(page, '/ingestion', /Document Ingestion - TechieRag/);

    const statsGrid = ingestionStatsGrid(page);
    await expect(statsGrid).toBeVisible({ timeout: 30000 });
    // Stat values in DOM order: Documents, Chunks, Storage Size, Last Ingestion.
    const statValues = statsGrid.locator('div.text-xl.font-bold');
    await expect(statValues.nth(0)).toHaveText(String(before + 1), { timeout: 30000 });
    const chunksText = (await statValues.nth(1).innerText()).trim();
    expect(parseInt(chunksText, 10)).toBeGreaterThan(0);

    // Ingested Documents table lists the temp doc with a non-empty chunk count.
    const docRow = page.getByRole('row').filter({ hasText: TMP_DOC });
    await expect(docRow).toBeVisible({ timeout: 30000 });
    const cells = docRow.getByRole('cell');
    expect(await cells.count()).toBeGreaterThanOrEqual(2);
    for (const cellText of await cells.allInnerTexts()) {
      expect(cellText.trim().length).toBeGreaterThan(0);
    }

    await page.screenshot({ path: 'test-results/screens/rag-ingest.png' });

    // --- Cleanup: /ingestion has NO per-row delete UI, but /text-ingestion's Documents
    // sidebar has a per-row trash button — delete ONLY the temp doc there. ---
    await gotoAndSettle(page, '/text-ingestion', /Text Ingestion - TechieRag/);

    const sidebarRow = page
      .locator('div.flex.items-center.justify-between')
      .filter({ has: page.getByText(TMP_DOC, { exact: true }) });
    await expect(sidebarRow).toBeVisible({ timeout: 30000 });
    await sidebarRow.getByRole('button').click(); // the row's only button is the trash icon

    await expect(page.getByText('Document deleted.')).toBeVisible({ timeout: 30000 });
    await expect(textIngestionDocsStat(page)).toHaveText(String(before), { timeout: 30000 });
    await expect(page.getByText(TMP_DOC, { exact: true })).toHaveCount(0);
  });

  test('REQ-UI-005 chat in Auto-RAG streaming mode returns answer, sources, and non-zero session tokens', async ({ page }) => {
    test.setTimeout(120000);
    await gotoAndSettle(page, '/chat', /RAG Chat/);

    // Open the Chat Configuration collapsible to verify/adjust mode + streaming.
    await page.getByText('Chat Configuration').click();

    // Mode defaults to "auto-rag"; only interact with the Select if it is not already Auto-RAG.
    const configCard = page.locator('div').filter({ hasText: /^Mode/ }).first();
    if (!(await page.getByText('Auto-RAG', { exact: true }).first().isVisible().catch(() => false))) {
      await configCard.locator('button').first().click();
      await page.getByText('Auto-RAG', { exact: true }).last().click();
    }

    // Streaming defaults ON; flip the switch only if the label reads "Off".
    const streamingLabel = page.locator('label[for="streaming"]');
    await expect(streamingLabel).toBeVisible({ timeout: 15000 });
    if ((await streamingLabel.innerText()).trim() === 'Off') {
      await page.locator('#streaming').click();
      await expect(streamingLabel).toHaveText('On', { timeout: 10000 });
    }

    // Ask the question (blur so the Blazor binding commits and the send button enables).
    const input = page.getByPlaceholder('Ask a question...');
    await fillAndCommit(page, input, 'What information is in the ingested documents? Answer in one sentence.');

    const sendButton = page.locator('div.mt-4.flex.gap-2 button');
    await expect(sendButton).toBeEnabled({ timeout: 15000 });
    await sendButton.click();

    // The final assistant message (with sources) appears once streaming completes — allow 90s.
    const sourcesTrigger = page.getByText(/Sources Used \(\d+\)/);
    await expect(sourcesTrigger).toBeVisible({ timeout: 90000 });

    // Assistant bubble has non-empty text.
    const assistantText = page.locator('.bg-muted .whitespace-pre-wrap').last();
    expect((await assistantText.innerText()).trim().length).toBeGreaterThan(0);

    // At least one source with a relevance score (Badge rendered as "NN%" via P0 format).
    const sourcesCount = parseInt((await sourcesTrigger.innerText()).match(/\((\d+)\)/)![1], 10);
    expect(sourcesCount).toBeGreaterThanOrEqual(1);
    await sourcesTrigger.click(); // expand the sources panel
    await expect(page.getByText(/\d+\s*%/).first()).toBeVisible({ timeout: 10000 });

    // Session footer token counter moved off zero.
    const sessionCounter = page.getByText(/Session: [\d,]+ tokens/);
    await expect(sessionCounter).toBeVisible({ timeout: 15000 });
    await expect(sessionCounter).not.toHaveText('Session: 0 tokens');

    await page.screenshot({ path: 'test-results/screens/rag-chat-sources.png' });
  });

  test('REQ-UI-008 token usage dashboard shows non-zero live usage for the model', async ({ page }) => {
    test.setTimeout(120000);
    await gotoAndSettle(page, '/token-usage', /Token Usage/);

    // Summary cards grid; value order matches label order: Total Tokens, Input/Output, Cost, Operations.
    const summaryGrid = page.locator('div.grid').filter({ hasText: 'Total Tokens' }).first();
    await expect(summaryGrid).toBeVisible({ timeout: 30000 });
    await expect(summaryGrid.getByText('Total Tokens', { exact: true })).toBeVisible();
    await expect(summaryGrid.getByText('Operations', { exact: true })).toBeVisible();

    const values = summaryGrid.locator('div.text-2xl.font-bold');
    await expect(values).toHaveCount(4);

    const totalTokens = parseInt((await values.nth(0).innerText()).replace(/,/g, ''), 10);
    const operations = parseInt((await values.nth(3).innerText()).replace(/,/g, ''), 10);
    expect(totalTokens).toBeGreaterThan(0); // connection test + chat ran earlier in this app session
    expect(operations).toBeGreaterThan(0);

    // Usage by Model table must NOT be the empty state, and must list the live model.
    await expect(page.getByText('No usage data yet.')).toHaveCount(0);
    const modelRow = page.getByRole('row').filter({ hasText: MODEL });
    await expect(modelRow).toBeVisible({ timeout: 30000 });
    const cells = modelRow.getByRole('cell');
    expect(await cells.count()).toBeGreaterThanOrEqual(5); // Model, Requests, Input, Output, Cost
    for (const cellText of await cells.allInnerTexts()) {
      expect(cellText.trim().length).toBeGreaterThan(0);
    }

    await page.screenshot({ path: 'test-results/screens/token-usage-live.png' });
  });
});
