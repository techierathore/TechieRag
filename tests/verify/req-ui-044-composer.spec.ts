import { test, expect, Page } from '@playwright/test';

/**
 * REQ-UI-044 / BRD-137 — multi-line chat composer with per-turn mode, model and scope.
 *
 * Acceptance under test, verbatim:
 *   "The input accepts multi-line text and grows (to roughly 12 lines); Return sends and
 *    Shift+Return inserts a newline."
 *   "The mode selector offers all FIVE modes — Auto-RAG / Query / Chat / Direct-LLM / Agent."
 *   "A retrieval-scope picker restricts retrieval to: whole workspace / pinned documents only /
 *    a chosen set of documents."
 *   "Plus attach and saved-prompts affordances."
 *
 * Run against a live app booted with ASPNETCORE_ENVIRONMENT=Development on :5124.
 * NOTE: this host has no LLM provider, so a completed streamed answer is NOT observable. The spec
 * asserts the turn was DISPATCHED (the composer clears and the user turn is rendered), never that
 * an answer arrived.
 */

const BASE = 'http://localhost:5124';
const COMPOSER = '#workspace-composer';

test.setTimeout(120000);

async function openWorkspaceChat(page: Page) {
  await page.goto(`${BASE}/`);
  const link = page.locator('a[href^="/workspace/"]').first();
  await expect(link).toBeVisible({ timeout: 30000 });
  const href = await link.getAttribute('href');
  await page.goto(`${BASE}${href}`);
  await expect(page.locator(COMPOSER)).toBeVisible({ timeout: 30000 });
}

async function ensureThread(page: Page) {
  const disabled = await page.locator(COMPOSER).isDisabled();
  if (disabled) {
    await page.getByRole('button', { name: 'New thread' }).click();
    await expect(page.locator(COMPOSER)).toBeEnabled({ timeout: 15000 });
  }
}

async function assertNoOverflow(page: Page, width: number) {
  const scrollW = await page.evaluate(() => document.documentElement.scrollWidth);
  expect(scrollW, `document scrolls horizontally at ${width}px`).toBeLessThanOrEqual(width + 2);
}

test('composer accepts multi-line text and grows toward 12 lines', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await openWorkspaceChat(page);
  await ensureThread(page);

  const composer = page.locator(COMPOSER);
  const startHeight = await composer.evaluate(el => (el as HTMLTextAreaElement).clientHeight);

  await composer.click();
  for (let i = 0; i < 8; i++) {
    await page.keyboard.type(`line ${i}`);
    await page.keyboard.press('Shift+Enter');
  }

  const grownHeight = await composer.evaluate(el => (el as HTMLTextAreaElement).clientHeight);
  const value = await composer.inputValue();

  expect(value.split('\n').length, 'Shift+Return did not insert newlines').toBeGreaterThanOrEqual(8);
  expect(grownHeight, 'composer did not grow with content').toBeGreaterThan(startHeight);

  // It must stop growing at the cap rather than pushing the page.
  const cap = await composer.evaluate(el => parseFloat(getComputedStyle(el).maxHeight));
  expect(grownHeight).toBeLessThanOrEqual(cap + 2);
  await assertNoOverflow(page, 1280);

  // The composer's own controls must not be pushed below the fold by a grown input.
  const attach = await page.getByRole('button', { name: 'Attach' }).boundingBox();
  expect(attach, 'Attach button is not laid out').not.toBeNull();
  expect(attach!.y + attach!.height, 'composer footer fell below the 900px fold').toBeLessThanOrEqual(900);

  // The mode selector must show its friendly label, not the raw enum name.
  await expect(page.getByText('Auto-RAG — retrieve, then answer with citations').first()).toBeVisible();

  await page.screenshot({ path: 'test-results/req-ui-044-multiline-1280.png', fullPage: false });
});

test('Return sends and Shift+Return inserts a newline', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await openWorkspaceChat(page);
  await ensureThread(page);

  const composer = page.locator(COMPOSER);
  await composer.click();

  // Shift+Return must NOT send.
  await page.keyboard.type('first line');
  await page.keyboard.press('Shift+Enter');
  await page.keyboard.type('second line');
  expect(await composer.inputValue()).toBe('first line\nsecond line');

  // Return must send: the composer clears and the typed text appears as a user turn.
  await page.keyboard.press('Enter');
  await expect(page.getByText('second line').first()).toBeVisible({ timeout: 30000 });
  await expect.poll(async () => composer.inputValue(), { timeout: 15000 }).toBe('');

  await page.screenshot({ path: 'test-results/req-ui-044-return-sends-1280.png' });
});

test('all five answering modes are offered and selectable', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await openWorkspaceChat(page);

  await page.getByLabel('Answering mode for this turn').click();
  const options = page.getByRole('option');
  await expect(options.first()).toBeVisible({ timeout: 15000 });

  const texts = (await options.allTextContents()).map(t => t.trim());
  for (const label of ['Auto-RAG', 'Query', 'Chat', 'Direct LLM', 'Agent']) {
    expect(texts.some(t => t.startsWith(label)), `mode "${label}" missing from ${JSON.stringify(texts)}`).toBe(true);
  }

  await page.screenshot({ path: 'test-results/req-ui-044-modes-open-1280.png' });

  // Selecting a mode must change what the page says the turn will do.
  await options.filter({ hasText: 'Direct LLM' }).first().click();
  await expect(page.getByText('no retrieval — the workspace documents are not consulted')).toBeVisible({ timeout: 15000 });

  await page.getByLabel('Answering mode for this turn').click();
  await page.getByRole('option').filter({ hasText: 'Query' }).first().click();
  await expect(page.getByText(/Mode: Query/)).toBeVisible({ timeout: 15000 });
});

test('retrieval-scope picker offers whole workspace, pinned only and chosen documents', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await openWorkspaceChat(page);

  await page.getByText('Choose the retrieval scope for this turn').click();
  await expect(page.getByText(/Whole workspace \(\d+ documents\)/)).toBeVisible({ timeout: 15000 });
  await expect(page.getByText(/Pinned documents only \(\d+\)/)).toBeVisible();
  await expect(page.getByText('Choose documents…')).toBeVisible();

  await page.screenshot({ path: 'test-results/req-ui-044-scope-open-1280.png' });

  await page.getByText(/Pinned documents only \(\d+\)/).click();
  await expect(page.getByText('retrieval scoped to pinned documents only')).toBeVisible({ timeout: 15000 });
});

test('per-turn model menu and saved prompts are reachable', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await openWorkspaceChat(page);
  await ensureThread(page);

  await page.getByText('Choose the model for this turn').click();
  await expect(page.getByText('This turn only')).toBeVisible({ timeout: 15000 });
  await page.screenshot({ path: 'test-results/req-ui-044-model-menu-1280.png' });
  await page.keyboard.press('Escape');

  await page.getByText('Insert a saved prompt').click();
  await expect(page.getByText('Saved prompts')).toBeVisible({ timeout: 15000 });
  await page.getByRole('menuitem', { name: 'Summarise' }).click();
  await expect.poll(async () => page.locator(COMPOSER).inputValue(), { timeout: 15000 })
    .toContain('Summarise the key points');

  await expect(page.getByRole('button', { name: 'Attach' })).toBeVisible();
});

/**
 * The one end-to-end proof available on a host with no reachable LLM provider: Query mode with
 * nothing in retrieval scope returns the library's deterministic "not covered" answer WITHOUT
 * calling a provider, while Auto-RAG on the same empty scope tries to reach one. Selecting the
 * mode therefore demonstrably changes what the turn does.
 */
test('Query mode chosen for one turn changes what the turn actually does', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await openWorkspaceChat(page);
  await ensureThread(page);

  await page.getByText('Choose the retrieval scope for this turn').click();
  await page.getByText(/Pinned documents only \(\d+\)/).click();

  await page.getByLabel('Answering mode for this turn').click();
  await page.getByRole('option').filter({ hasText: 'Query' }).first().click();
  await expect(page.getByText(/Mode: Query/)).toBeVisible({ timeout: 15000 });

  await page.locator(COMPOSER).click();
  await page.keyboard.type('what do the contracts say about liability');
  await page.keyboard.press('Enter');

  await expect(page.getByText(/do not contain information relevant/i))
    .toBeVisible({ timeout: 30000 });

  await page.screenshot({ path: 'test-results/req-ui-044-query-mode-turn-1280.png' });
});

test('composer renders without overlap or clipping at 900px', async ({ page }) => {
  await page.setViewportSize({ width: 900, height: 900 });
  await openWorkspaceChat(page);
  await ensureThread(page);
  await assertNoOverflow(page, 900);

  // Every composer control must be on-screen and non-zero.
  const boxes = await page.evaluate(() => {
    const root = document.querySelector('.td-composer')!;
    return Array.from(root.querySelectorAll('button, textarea, [class*="td-composer-chip"]'))
      .filter(e => (e as HTMLElement).offsetParent !== null)
      .map(e => {
        const r = e.getBoundingClientRect();
        return { tag: e.tagName, text: (e.textContent || '').slice(0, 24), x: r.x, y: r.y, w: r.width, h: r.height };
      });
  });

  expect(boxes.length).toBeGreaterThan(4);
  for (const b of boxes) {
    expect(b.w, `${b.tag} "${b.text}" has zero width`).toBeGreaterThan(0);
    expect(b.h, `${b.tag} "${b.text}" has zero height`).toBeGreaterThan(0);
    expect(b.x + b.w, `${b.tag} "${b.text}" spills past 900px`).toBeLessThanOrEqual(902);
    expect(b.x, `${b.tag} "${b.text}" starts off-canvas`).toBeGreaterThanOrEqual(-2);
  }

  await page.screenshot({ path: 'test-results/req-ui-044-composer-900.png', fullPage: false });
});
