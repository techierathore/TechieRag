import { test, expect, chromium, Browser, Page } from '@playwright/test';
import * as fs from 'fs';

// REQ-NFR-003 / BRD-94 — >=25 concurrent chat users.
//
// Each Blazor Server user is a stateful SignalR circuit held in server memory, so the real risk
// is circuit exhaustion / memory pressure rather than request throughput. This drives N genuine
// browser contexts (separate circuits, separate cookies) against the live app, confirms every
// circuit reaches interactivity, exercises a real interaction on each, and asserts none of them
// dropped into Blazor's reconnect state.

const BASE = process.env.TD_BASE ?? 'http://localhost:5132';
const SLUG = process.env.TD_SLUG ?? 'default';
const USERS = Number(process.env.TD_USERS ?? 25);
const SHOT = 'test-results/screens';

test.describe('REQ-NFR-003 concurrent circuits', () => {
  test.setTimeout(600000);

  test(`${USERS} concurrent Blazor Server circuits stay live and responsive`, async () => {
    const browser: Browser = await chromium.launch({ headless: true });
    const pages: Page[] = [];
    const connectMs: number[] = [];
    const interactMs: number[] = [];
    const failures: string[] = [];

    try {
      // Phase 1 — open every circuit concurrently. This is the memory-hungry part.
      const openStart = performance.now();
      await Promise.all(
        Array.from({ length: USERS }, async (_unused, i) => {
          const context = await browser.newContext();
          const page = await context.newPage();
          pages.push(page);
          const t0 = performance.now();
          try {
            await page.goto(`${BASE}/workspace/${SLUG}`, { timeout: 120000 });
            await page.waitForFunction(() => (window as any).Blazor !== undefined, null, { timeout: 120000 });
            await expect(page.getByText('Threads', { exact: true })).toBeVisible({ timeout: 120000 });
            connectMs.push(performance.now() - t0);
          } catch (err) {
            failures.push(`circuit ${i} failed to connect: ${(err as Error).message.split('\n')[0]}`);
          }
        }),
      );
      const openTotal = performance.now() - openStart;
      console.log(`CONC connected ${connectMs.length}/${USERS} circuits in ${openTotal.toFixed(0)} ms`);

      // Phase 2 — every circuit performs a real interaction at the same time.
      await Promise.all(
        pages.map(async (page, i) => {
          try {
            const t0 = performance.now();
            await page.getByRole('button', { name: 'New thread' }).click({ timeout: 60000 });
            await expect
              .poll(() => page.locator('.td-chat-grid button.min-w-0').count(),
                { timeout: 60000, intervals: [25] })
              .toBeGreaterThan(0);
            interactMs.push(performance.now() - t0);
          } catch (err) {
            failures.push(`circuit ${i} interaction failed: ${(err as Error).message.split('\n')[0]}`);
          }
        }),
      );

      // Phase 3 — no circuit may have dropped. Blazor renders a reconnect modal when it does.
      let dropped = 0;
      for (const page of pages) {
        const visible = await page.locator('#components-reconnect-modal')
          .isVisible().catch(() => false);
        if (visible) dropped++;
      }

      const sorted = [...interactMs].sort((a, b) => a - b);
      const p50 = sorted[Math.floor(sorted.length * 0.5)] ?? NaN;
      const p95 = sorted[Math.floor(sorted.length * 0.95)] ?? NaN;
      const summary = {
        requestedUsers: USERS,
        circuitsConnected: connectMs.length,
        circuitsInteractedOk: interactMs.length,
        circuitsDroppedToReconnect: dropped,
        connectMsMax: Math.max(...connectMs),
        interactMsP50: p50,
        interactMsP95: p95,
        interactMsMax: Math.max(...interactMs),
        failures,
      };
      console.log('CONC summary', JSON.stringify(summary, null, 2));
      fs.mkdirSync('test-results', { recursive: true });
      fs.writeFileSync('test-results/req-nfr-003-concurrency.json', JSON.stringify(summary, null, 2));

      await pages[0].screenshot({ path: `${SHOT}/req-nfr-003-concurrency.png`, fullPage: true });

      expect(connectMs.length, `only ${connectMs.length}/${USERS} circuits connected`).toBe(USERS);
      expect(interactMs.length, `only ${interactMs.length}/${USERS} circuits interacted`).toBe(USERS);
      expect(dropped, `${dropped} circuits dropped into reconnect`).toBe(0);
    } finally {
      await browser.close();
    }
  });
});
