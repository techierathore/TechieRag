import { test, expect } from '@playwright/test';

/**
 * Probes whether the AppDb path mismatch (REQ-FN-034) reaches the user-facing UI.
 * The migrator writes a CWD-relative database while the app reads a BaseDirectory-relative
 * one, so app-DB-backed screens query a schema-less file.
 */
test('workspace settings screen against the app-resolved AppDb', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', e => errors.push(String(e)));

  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto('/workspace/default/settings', { waitUntil: 'networkidle' });
  await page.waitForTimeout(1500);

  const body = await page.locator('body').innerText();
  console.log('--- BODY (first 700 chars) ---');
  console.log(body.slice(0, 700));
  console.log('--- pageerrors ---', errors);

  await page.screenshot({ path: 'test-results/screens/fn034-workspace-settings.png', fullPage: true });

  // Report, don't assert — this probe exists to characterise the defect.
  const brokenMarkers = ['no such table', 'not available', 'does not exist', 'Unhandled'];
  const hit = brokenMarkers.filter(m => body.toLowerCase().includes(m.toLowerCase()));
  console.log('--- broken markers present ---', hit);
});
