import { test, expect } from '@playwright/test';
import * as fs from 'fs';

// REQ-FN-010 / REQ-FN-011 — workspace-chat thread export (Markdown/JSON) and
// delete-all-history, driven against the live TechieDesk app on :5111.
const BASE = 'http://localhost:5111';
const SLUG = 'default';
const SHOT = 'test-results/screens';

test.setTimeout(60000);

async function openThreadMenu(page) {
  await page.goto(`${BASE}/workspace/${SLUG}`);
  await page.waitForLoadState('networkidle');
  await page.waitForFunction(() => document.title.includes('Chat'), null, { timeout: 20000 });
  await page.waitForTimeout(500);
  // open the first thread's actions menu
  await page.getByRole('button', { name: 'Thread actions' }).first().click();
}

test('REQ-FN-010 thread menu shows Export Markdown + JSON and downloads a file', async ({ page }) => {
  await openThreadMenu(page);

  await expect(page.getByText('Export as Markdown', { exact: true })).toBeVisible({ timeout: 10000 });
  await expect(page.getByText('Export as JSON', { exact: true })).toBeVisible();
  await page.screenshot({ path: `${SHOT}/req-fn-010-menu.png`, fullPage: true });

  // Markdown download
  const mdDownload = page.waitForEvent('download', { timeout: 15000 });
  await page.getByText('Export as Markdown', { exact: true }).click();
  const md = await mdDownload;
  const mdPath = await md.path();
  const mdText = fs.readFileSync(mdPath, 'utf8');
  console.log('MD file:', md.suggestedFilename(), 'len', mdText.length);
  expect(md.suggestedFilename()).toMatch(/\.md$/);
  expect(mdText).toContain('# Workspace chat questions');
  expect(mdText).toContain('## User');
  expect(mdText).toContain('## Assistant');

  // JSON download
  await page.getByRole('button', { name: 'Thread actions' }).first().click();
  const jsonDownload = page.waitForEvent('download', { timeout: 15000 });
  await page.getByText('Export as JSON', { exact: true }).click();
  const js = await jsonDownload;
  const jsText = fs.readFileSync(await js.path(), 'utf8');
  console.log('JSON file:', js.suggestedFilename(), 'len', jsText.length);
  expect(js.suggestedFilename()).toMatch(/\.json$/);
  const parsed = JSON.parse(jsText);
  expect(parsed.title).toBe('Workspace chat questions');
  expect(parsed.messages.length).toBeGreaterThanOrEqual(2);
});

test('REQ-FN-011 delete-all-history confirm dialog opens', async ({ page }) => {
  await page.goto(`${BASE}/workspace/${SLUG}`);
  await page.waitForLoadState('networkidle');
  await page.waitForFunction(() => document.title.includes('Chat'), null, { timeout: 20000 });
  await page.waitForTimeout(500);

  await page.getByRole('button', { name: 'Delete all my history' }).click();
  await expect(page.getByText('Delete all my history?', { exact: true })).toBeVisible({ timeout: 10000 });
  await expect(page.getByRole('button', { name: 'Delete everything' })).toBeVisible();
  await page.screenshot({ path: `${SHOT}/req-fn-011-confirm.png`, fullPage: true });
  // cancel — do NOT wipe data during this render check
  await page.getByRole('button', { name: 'Cancel' }).click();
});

for (const width of [1280, 390]) {
  test(`render clean @${width} — no horizontal overflow`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`${BASE}/workspace/${SLUG}`);
    await page.waitForLoadState('networkidle');
    await page.waitForFunction(() => document.title.includes('Chat'), null, { timeout: 20000 });
    await page.waitForTimeout(500);
    await page.screenshot({ path: `${SHOT}/req-fn-workspace-${width}.png`, fullPage: true });
    const scrollWidth = await page.evaluate(() =>
      Math.max(document.documentElement.scrollWidth, document.body.scrollWidth)
    );
    expect(scrollWidth, `overflows ${width}px (scrollWidth=${scrollWidth})`).toBeLessThanOrEqual(width + 5);
  });
}
