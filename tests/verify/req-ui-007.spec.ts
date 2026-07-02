import { test, expect, Page } from '@playwright/test';

test.setTimeout(180000);

/**
 * REQ-UI-007 — Tool Demo page (agent loop, execution trace).
 * Acceptance: the LLM agent loop calls registered tools and the Execution Trace
 * renders each real step LIVE (tool request + tool execution + final answer),
 * not the hardcoded single-step fallback ("LLM generated final answer" alone).
 * Includes the §4a render gate and §4b visual-truth gate (1280x800 + 390x844).
 */

async function assertNoOverlapAndInViewport(page: Page, width: number) {
  // the page itself must never pan horizontally
  const scrollW = await page.evaluate(() => document.documentElement.scrollWidth);
  expect(scrollW, `document scrolls horizontally at ${width}px viewport`).toBeLessThanOrEqual(width + 2);

  const boxes = await page.evaluate(() => {
    const inScrollContainer = (el: Element) => {
      for (let p = el.parentElement; p && p !== document.body; p = p.parentElement) {
        const o = getComputedStyle(p).overflowX;
        if (o === 'auto' || o === 'scroll') return true;
      }
      return false;
    };
    const els = Array.from(document.querySelectorAll('h4, table, button, input, textarea'))
      .filter(e => (e as HTMLElement).offsetParent !== null);
    return els.map(e => {
      const r = e.getBoundingClientRect();
      return { tag: e.tagName, text: (e.textContent || '').slice(0, 30), x: r.x, y: r.y, w: r.width, h: r.height, scrollable: inScrollContainer(e) };
    });
  });
  for (const b of boxes) {
    expect(b.w, `${b.tag} "${b.text}" has zero width`).toBeGreaterThan(0);
    expect(b.h, `${b.tag} "${b.text}" has zero height`).toBeGreaterThan(0);
    if (!b.scrollable) {
      expect(b.x + b.w, `${b.tag} "${b.text}" spills past viewport ${width}px`).toBeLessThanOrEqual(width + 2);
      expect(b.x, `${b.tag} "${b.text}" starts off-canvas`).toBeGreaterThanOrEqual(-2);
    }
  }
  // trace step cards must not overlap each other
  const steps = await page.locator('div.rounded-md.border.p-3').all();
  const rects = [];
  for (const s of steps) { const r = await s.boundingBox(); if (r) rects.push(r); }
  for (let i = 0; i < rects.length; i++) {
    for (let j = i + 1; j < rects.length; j++) {
      const a = rects[i], b = rects[j];
      const xOverlap = Math.max(0, Math.min(a.x + a.width, b.x + b.width) - Math.max(a.x, b.x));
      const yOverlap = Math.max(0, Math.min(a.y + a.height, b.y + b.height) - Math.max(a.y, b.y));
      expect(Math.min(xOverlap, yOverlap), `trace steps ${i} and ${j} overlap`).toBeLessThanOrEqual(4);
    }
  }
}

test('REQ-UI-007 render gate: Available Tools table renders live data', async ({ page }) => {
  await page.goto('/tool-demo');
  await page.waitForLoadState('networkidle');
  await page.waitForFunction(() => document.title.includes('Tool Calling Demo'), null, { timeout: 15000 });

  const rows = page.locator('table tbody tr');
  await expect(rows.first()).toBeVisible({ timeout: 20000 });
  const rowCount = await rows.count();
  expect(rowCount, 'Available Tools table must list the 4 demo tools').toBeGreaterThanOrEqual(4);
  for (let i = 0; i < rowCount; i++) {
    const cells = await rows.nth(i).locator('td').allInnerTexts();
    expect(cells.some(c => c.trim().length > 0), `row ${i} renders blank cells`).toBe(true);
  }
  await expect(page.getByText('get_weather')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Run Agent Loop' })).toBeVisible();
});

test('REQ-UI-007 execution trace renders real tool steps live (not the fallback)', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto('/tool-demo');
  await page.waitForLoadState('networkidle');
  await page.waitForFunction(() => document.title.includes('Tool Calling Demo'), null, { timeout: 15000 });
  await page.waitForTimeout(400);

  await page.getByPlaceholder(/What's the weather/i)
    .fill('What is the current weather in Tokyo? You must call the get_weather tool to find out, then report it.');
  await page.getByRole('button', { name: 'Run Agent Loop' }).click();

  await expect(page.getByText('Execution Trace', { exact: true })).toBeVisible({ timeout: 120000 });
  await expect(page.getByText('Final Answer', { exact: true })).toBeVisible({ timeout: 120000 });
  await page.waitForTimeout(800);

  const stepCards = page.locator('div.rounded-md.border.p-3');
  const stepTexts = await stepCards.allInnerTexts();
  console.log('TRACE STEPS =>', JSON.stringify(stepTexts, null, 1));

  // fallback-only trace = exactly one step saying "LLM generated final answer" with no tool steps → FAIL
  const hasToolRequest = stepTexts.some(t => /requested tool/i.test(t));
  const hasToolExecution = stepTexts.some(t => /Executed get_weather/i.test(t));
  expect(hasToolRequest, 'trace must show an "LLM requested tool(s)" step').toBe(true);
  expect(hasToolExecution, 'trace must show an "Executed get_weather(...)" step').toBe(true);
  expect(stepTexts.length, 'trace must show multiple real steps, not the single fallback').toBeGreaterThanOrEqual(3);

  // the executed step must render its non-empty result block (render gate on the trace itself)
  const executedStep = stepCards.filter({ hasText: 'Executed get_weather' }).first();
  const resultBlock = executedStep.locator('div.font-mono');
  await expect(resultBlock).toBeVisible();
  expect((await resultBlock.innerText()).trim().length).toBeGreaterThan(0);

  // final answer must use the tool result (mock returns "32°C, Partly Cloudy, Humidity: 65%")
  const finalAnswer = await page.locator('div.whitespace-pre-wrap').innerText();
  expect(finalAnswer, 'final answer must reflect the tool result, not a hallucination')
    .toMatch(/32|Partly Cloudy|65%/i);

  // §4b visual truth — desktop
  await assertNoOverlapAndInViewport(page, 1280);
  await page.screenshot({ path: 'test-results/screens/req-ui-007-trace-desktop.png', fullPage: true });

  // §4b visual truth — mobile (state persists on resize; Blazor circuit intact)
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(600);
  await expect(page.getByText('Execution Trace', { exact: true })).toBeVisible();
  await assertNoOverlapAndInViewport(page, 390);
  await page.screenshot({ path: 'test-results/screens/req-ui-007-trace-mobile.png', fullPage: true });
});
