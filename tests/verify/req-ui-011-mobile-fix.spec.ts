import { test, expect } from '@playwright/test';

// REQ-UI-011 mobile visual defect fix (verifier 2026-07-02): with a RUNNING Qdrant
// container, the Container-Management row's action buttons sat off-canvas at 390px
// (x 666..765). Fix = TR-004 containment: wrap the page's DataTables in
// <div class="relative overflow-x-auto">. Accepted pass condition: page scrollWidth
// stays at the viewport AND every action button is either fully within [0,390] or
// inside a local overflow-x-auto wrapper.

async function waitForRunningRow(page) {
  await page.goto('/qdrant-admin');
  await page.waitForLoadState('networkidle');
  await page.waitForFunction(() => document.title.includes('Qdrant Admin'), { timeout: 20000 });
  // Auto-detection connects to the running container on init; wait for the
  // containers table to show the Running row with its Stop button.
  await expect(page.getByRole('button', { name: 'Stop', exact: true })).toBeVisible({ timeout: 30000 });
  await page.waitForTimeout(600);
}

test('REQ-UI-011 mobile 390px — running-container row contained', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await waitForRunningRow(page);

  const scrollWidth = await page.evaluate(() =>
    Math.max(document.documentElement.scrollWidth, document.body.scrollWidth)
  );
  expect(scrollWidth, `page scrollWidth must equal viewport (got ${scrollWidth})`).toBeLessThanOrEqual(390);

  // Every button in the container-management actions row must be within [0,390]
  // or inside the local overflow-x-auto wrapper (accepted TR-004 containment).
  const stopBtn = page.getByRole('button', { name: 'Stop', exact: true });
  const rowButtons = stopBtn.locator('xpath=ancestor::div[contains(@class,"flex")][1]//button');
  const count = await rowButtons.count();
  expect(count).toBeGreaterThanOrEqual(2); // Stop + connect(plug) icon button
  for (let i = 0; i < count; i++) {
    const btn = rowButtons.nth(i);
    const info = await btn.evaluate((el) => {
      const r = el.getBoundingClientRect();
      let node = el.parentElement;
      let contained = false;
      while (node) {
        const cs = getComputedStyle(node);
        if (cs.overflowX === 'auto' || cs.overflowX === 'scroll') { contained = true; break; }
        node = node.parentElement;
      }
      return { left: r.left, right: r.right, contained };
    });
    const withinViewport = info.left >= 0 && info.right <= 390;
    expect(
      withinViewport || info.contained,
      `button ${i} at x ${info.left}..${info.right} must be on-canvas or inside overflow-x-auto`
    ).toBe(true);
  }

  await page.screenshot({ path: 'test-results/screens/req-ui-011-fixed-mobile.png', fullPage: true });
});

test('REQ-UI-011 desktop 1280px — no regression', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await waitForRunningRow(page);

  const scrollWidth = await page.evaluate(() =>
    Math.max(document.documentElement.scrollWidth, document.body.scrollWidth)
  );
  expect(scrollWidth, `desktop scrollWidth must equal viewport (got ${scrollWidth})`).toBeLessThanOrEqual(1280);

  const stopBox = await page.getByRole('button', { name: 'Stop', exact: true }).boundingBox();
  expect(stopBox).not.toBeNull();
  expect(stopBox!.x).toBeGreaterThanOrEqual(0);
  expect(stopBox!.x + stopBox!.width).toBeLessThanOrEqual(1280);

  await page.screenshot({ path: 'test-results/screens/req-ui-011-fixed-desktop.png', fullPage: true });
});
