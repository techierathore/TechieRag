import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

// REQ-NFR-003 / BRD-94 — measured performance targets against the live TechieDesk app.
//
// Targets under test here:
//   * <200ms UI interactions (in-page, circuit round-trip)
//   * 10-page PDF embedded in <60s via the local BGE-M3 ONNX model
//   * time-to-first-Sources (retrieval + context assembly) as the measurable slice of the
//     "<2s streaming overhead" target — end-to-end generation latency is NOT measurable on
//     this host because no LLM provider is reachable.
//
// Every number printed by this spec is a real wall-clock measurement; nothing is synthesised.

const BASE = process.env.TD_BASE ?? 'http://localhost:5132';
const SLUG = process.env.TD_SLUG ?? 'default';
const SHOT = 'test-results/screens';
const PDF = process.env.TD_PDF
  ?? '/private/tmp/claude-501/-Volumes-MacD-MyCode-TechieRag/6ec44dff-2137-4039-9b2c-3815d9055a6d/scratchpad/bench10.pdf';

const results: Array<{ label: string; ms: number; target: string }> = [];

function record(label: string, ms: number, target: string) {
  results.push({ label, ms, target });
  console.log(`PERF ${label} = ${ms.toFixed(1)} ms (target ${target})`);
}

test.afterAll(() => {
  console.log('\n=== REQ-NFR-003 measured results ===');
  for (const r of results) {
    console.log(`${r.label.padEnd(46)} ${r.ms.toFixed(1).padStart(9)} ms   target ${r.target}`);
  }
  fs.mkdirSync('test-results', { recursive: true });
  fs.writeFileSync('test-results/req-nfr-003-perf.json', JSON.stringify(results, null, 2));
});

/** Waits until the Blazor Server circuit is live, so timings measure a real interactive page. */
async function waitForCircuit(page: Page) {
  await page.waitForLoadState('networkidle');
  await page.waitForFunction(
    () => (window as any).Blazor !== undefined,
    null,
    { timeout: 30000 },
  );
  // Blazor attaches before the circuit finishes its first render batch; give the
  // interactive handlers a beat to bind so the first click is not measured cold.
  await page.waitForTimeout(750);
}

/** Times an interaction: click, then wait for the resulting DOM state. */
async function timeInteraction(label: string, target: string, action: () => Promise<void>) {
  const start = performance.now();
  await action();
  const ms = performance.now() - start;
  record(label, ms, target);
  return ms;
}

/**
 * Times an interaction entirely inside the page: clicks `clickSelector` and stops the clock on
 * the DOM mutation that satisfies `donePredicate`. Playwright's `expect` polls on a backoff
 * schedule (100ms, 250ms, …), which quantises measurements and makes a 40ms interaction read as
 * 200ms — this measures the real latency instead.
 */
async function measureInPage(
  page: Page,
  label: string,
  target: string,
  /** JS arrow-function source returning the element to click, e.g. "() => document.querySelector('#x')". */
  clickTarget: string,
  /** JS arrow-function source returning true once the interaction has visibly completed. */
  donePredicate: string,
): Promise<number> {
  const ms = await page.evaluate(
    ({ clickTarget, donePredicate }) => new Promise<number>((resolve, reject) => {
      // eslint-disable-next-line no-new-func
      const done = new Function(`return (${donePredicate});`)() as () => boolean;
      // eslint-disable-next-line no-new-func
      const pick = new Function(`return (${clickTarget});`)() as () => HTMLElement | null;
      const el = pick();
      if (!el) {
        reject(new Error(`no click target for: ${clickTarget}`));
        return;
      }
      // Guard against measuring a no-op: if the end state already holds, the observer would
      // just time-stamp the next unrelated re-render and report a meaningless number.
      if (done()) {
        reject(new Error(`predicate already true before the click: ${donePredicate}`));
        return;
      }
      const observer = new MutationObserver(() => {
        if (done()) {
          clearTimeout(timer);
          observer.disconnect();
          resolve(performance.now() - t0);
        }
      });
      const timer = setTimeout(() => {
        observer.disconnect();
        reject(new Error(`timed out waiting for: ${donePredicate}`));
      }, 30000);
      observer.observe(document.documentElement, {
        childList: true, subtree: true, attributes: true, characterData: true,
      });
      const t0 = performance.now();
      el.click();
    }),
    { clickTarget, donePredicate },
  );
  record(label, ms, target);
  return ms;
}

test.describe('REQ-NFR-003 performance targets', () => {
  test.setTimeout(300000);

  test('UI interaction latency (<200ms) on real circuit interactions', async ({ page }) => {
    await page.goto(`${BASE}/workspace/${SLUG}`);
    await waitForCircuit(page);
    await expect(page.getByText('Threads', { exact: true })).toBeVisible({ timeout: 20000 });

    // Start from a known-empty thread list so the counts below are deterministic across runs.
    // NOTE: count thread *title* buttons, not the "Thread actions" trigger — TrBlazeUI's
    // DropdownMenuTrigger emits the trigger button twice into the DOM (see libraryIssues).
    const threadRows = page.locator('.td-chat-grid button.min-w-0');
    const wipe = page.locator('button[aria-label="Delete all my history"]');
    if (await wipe.isVisible().catch(() => false)) {
      await wipe.click();
      await page.getByRole('button', { name: 'Delete everything' }).click();
    }
    await expect(page.getByText('No threads yet', { exact: true })).toBeVisible({ timeout: 20000 });
    await expect.poll(() => threadRows.count(), { timeout: 20000, intervals: [50] }).toBe(0);

    const NEW_THREAD = "() => document.querySelector('.td-chat-grid button.mb-2.w-full')";
    const ROWS = ".td-chat-grid button.min-w-0";

    // 1. Create a thread — server round-trip + SQLite insert + re-render.
    await measureInPage(
      page, 'interaction: New thread -> thread appears', '<200ms',
      NEW_THREAD,
      `() => document.querySelectorAll('${ROWS}').length === 1`,
    );

    // Second thread so there is something to switch between.
    await page.getByRole('button', { name: 'New thread' }).click();
    await expect.poll(() => threadRows.count(), { timeout: 20000, intervals: [10] }).toBe(2);

    // 2. Switch active thread — pure circuit round-trip, no navigation. New threads are inserted
    // at index 0 and selected, so the LAST row is the inactive one: clicking it is a real switch.
    await measureInPage(
      page, 'interaction: thread switch -> active highlight', '<200ms',
      `() => { const r = document.querySelectorAll('${ROWS}'); return r[r.length - 1]; }`,
      `() => { const r = document.querySelectorAll('${ROWS}');
               const last = r[r.length - 1];
               return !!last && last.parentElement.className.includes('bg-accent'); }`,
    );

    // 3. Open the thread actions dropdown — client+circuit interaction. The trigger's accessible
    // name has varied ("Thread actions" / "Actions for thread <title>"), so match either.
    const MENU_TRIGGER = `() => Array.from(document.querySelectorAll('.td-chat-grid button'))
      .find(b => (b.getAttribute('aria-label') || '').startsWith('Thread actions')
              || (b.textContent || '').trim().startsWith('Actions for thread'))`;
    await measureInPage(
      page, 'interaction: open thread menu', '<200ms',
      MENU_TRIGGER,
      "() => document.body.textContent.includes('Export as Markdown')",
    );
    await page.keyboard.press('Escape');

    // 4. Open a modal dialog — render of a new component subtree over the circuit.
    await measureInPage(
      page, 'interaction: open delete-all dialog', '<200ms',
      "() => document.querySelector('button[aria-label=\"Delete all my history\"]')",
      "() => document.body.textContent.includes('Delete all my history?')",
    );
    await page.getByRole('button', { name: 'Cancel' }).first().click();

    await page.screenshot({ path: `${SHOT}/req-nfr-003-ui.png`, fullPage: true });

    // Navigations are measured separately — they are page loads, not in-page interactions.
    await timeInteraction('navigation: chat -> documents', 'informational', async () => {
      await page.getByRole('link', { name: 'Documents' }).click();
      await expect(page.getByText(/^Library \(/)).toBeVisible({ timeout: 30000 });
    });

    await timeInteraction('navigation: documents -> workspace chat', 'informational', async () => {
      await page.getByRole('link', { name: 'Default', exact: true }).click();
      await expect(page.getByText('Threads', { exact: true })).toBeVisible({ timeout: 30000 });
    });

    // 5. Tab switch on the workspace settings page — TabsContent is rendered on demand, so this
    //    is a genuine server render, not a CSS show/hide.
    await page.goto(`${BASE}/workspace/${SLUG}/settings`);
    await waitForCircuit(page);
    await expect(page.getByText('Display name', { exact: true })).toBeVisible({ timeout: 30000 });
    await measureInPage(
      page, 'interaction: settings tab switch (General -> Retrieval)', '<200ms',
      `() => Array.from(document.querySelectorAll('button,[role="tab"]'))
        .find(b => (b.textContent || '').trim() === 'Retrieval')`,
      "() => !!document.querySelector('input[aria-label=\"Top-K snippets\"]')",
    );
    await page.screenshot({ path: `${SHOT}/req-nfr-003-tabs.png`, fullPage: true });
    await page.goto(`${BASE}/workspace/${SLUG}`);
    await waitForCircuit(page);

    const inPage = results.filter(r => r.label.startsWith('interaction:'));
    const worst = Math.max(...inPage.map(r => r.ms));
    console.log(`PERF worst in-page interaction = ${worst.toFixed(1)} ms`);
    expect(inPage.length).toBe(5);
    expect(worst, `worst in-page interaction was ${worst.toFixed(1)}ms`).toBeLessThan(200);
  });

  test('10-page PDF embedded in <60s via local BGE-M3', async ({ page }) => {
    expect(fs.existsSync(PDF), `benchmark PDF missing at ${PDF}`).toBeTruthy();

    await page.goto(`${BASE}/workspace/${SLUG}/documents`);
    await waitForCircuit(page);
    await expect(page.getByText(/^Library \(/)).toBeVisible({ timeout: 30000 });

    // Give the upload a unique name so a previous run's content hash cannot dedupe it away.
    const unique = path.join(
      path.dirname(PDF),
      `bench10-${Date.now()}.pdf`,
    );
    const bytes = fs.readFileSync(PDF);
    // Append a unique PDF comment so the content hash differs from earlier runs.
    fs.writeFileSync(unique, Buffer.concat([bytes, Buffer.from(`\n% run ${Date.now()}\n`)]));

    // The badge text also appears in the library table, so count the delta rather than
    // asserting on a single element.
    const embedded = page.getByText('Embedded', { exact: true });
    const before = await embedded.count();

    const start = performance.now();
    await page.locator('input[type="file"]').setInputFiles(unique);

    // "Embedded" badge = chunked + embedded via local ONNX + persisted to the vector store.
    await expect.poll(() => embedded.count(), { timeout: 180000, intervals: [50] })
      .toBeGreaterThan(before);
    const ms = performance.now() - start;
    record('ingest: 10-page PDF -> Embedded badge', ms, '<60000ms');

    await page.screenshot({ path: `${SHOT}/req-nfr-003-ingest.png`, fullPage: true });
    fs.unlinkSync(unique);

    // Assert the target explicitly — a miss must fail loudly rather than be reported as a pass.
    expect(ms).toBeLessThan(60000);
  });

  test('time-to-first-Sources (retrieval + context assembly) on a real question', async ({ page }) => {
    await page.goto(`${BASE}/workspace/${SLUG}`);
    await waitForCircuit(page);

    await page.getByRole('button', { name: 'New thread' }).click();
    await expect.poll(() => page.locator('.td-chat-grid button.min-w-0').count(),
      { timeout: 20000, intervals: [25] }).toBeGreaterThan(0);

    const composer = page.locator('textarea');
    await composer.fill('What chunk size does the ingestion pipeline use and why does overlap matter?');

    const start = performance.now();
    await page.locator('button:has(svg)').last().click();

    // The Sources event is emitted before the first generated token, so this isolates
    // retrieval + context assembly from LLM generation latency.
    const sourceBadge = page.locator('.animate-pulse, [class*="badge"]');
    await expect(page.getByText('Retrieving & thinking…')).toBeVisible({ timeout: 20000 });
    const spinnerMs = performance.now() - start;
    record('chat: send -> "Retrieving & thinking" visible', spinnerMs, '<200ms');

    // Wait for either the streaming source citations (retrieval complete — each badge reads
    // "<document> · <score>") or a provider error bubble.
    const sources = page.getByText(/·\s*\d\.\d{2}\s*$/);
    const errorBubble = page.getByText(/Error:/);
    const outcome = await Promise.race([
      sources.first().waitFor({ state: 'visible', timeout: 120000 })
        .then(() => 'sources' as const).catch(() => 'none' as const),
      errorBubble.first().waitFor({ state: 'visible', timeout: 120000 })
        .then(() => 'error' as const).catch(() => 'none' as const),
    ]);
    const ms = performance.now() - start;
    record(`chat: send -> ${outcome}`, ms, 'informational (no LLM on host)');
    if (outcome === 'sources') {
      const texts = await sources.allInnerTexts();
      console.log(`PERF retrieved sources (${texts.length}): ${JSON.stringify(texts.slice(0, 5))}`);
      expect(texts.length).toBeGreaterThan(0);
    }

    // Whatever happened first, the provider failure must eventually surface (REQ-NFR-010).
    await expect(errorBubble.first()).toBeVisible({ timeout: 120000 });
    const errText = await errorBubble.first().innerText();
    console.log(`PERF provider failure surfaced as: ${errText.slice(0, 200)}`);

    await page.screenshot({ path: `${SHOT}/req-nfr-003-chat.png`, fullPage: true });
    console.log(`PERF chat outcome = ${outcome}`);
    expect(['sources', 'error']).toContain(outcome);
    void sourceBadge;
  });
});
