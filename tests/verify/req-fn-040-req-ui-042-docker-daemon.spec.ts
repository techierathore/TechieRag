import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

/**
 * Cluster self-smoke for REQ-FN-040 (configurable Docker daemon) + REQ-UI-042 (daemon endpoint
 * configuration screen), both BRD-134.
 *
 * There is very likely NO Docker daemon on this host. That is the point: the path that must be
 * driven is the honest-failure path. A wrong or blank error is a failure of the requirement.
 */

const BASE = 'http://localhost:5125';
const REPO = path.resolve(__dirname, '../..');
const SHOTS = path.join(REPO, 'test-results', 'screens');

async function shoot(page: Page, label: string) {
  fs.mkdirSync(SHOTS, { recursive: true });
  await page.screenshot({ path: path.join(SHOTS, `${label}.png`), fullPage: true });
}

async function openAdmin(page: Page, width: number) {
  await page.setViewportSize({ width, height: width < 500 ? 844 : 950 });
  await page.goto(`${BASE}/qdrant-admin`, { waitUntil: 'networkidle' });
  // Blazor Server: wait for the interactive circuit to have rendered the daemon card.
  await expect(page.locator('#active-daemon-endpoint')).toBeVisible({ timeout: 30000 });
}

test('daemon configuration renders and the active daemon is shown', async ({ page }) => {
  await openAdmin(page, 1280);

  await expect(page.getByText('Docker daemon', { exact: true }).first()).toBeVisible();

  const endpoint = (await page.locator('#active-daemon-endpoint').innerText()).trim();
  console.log('ACTIVE ENDPOINT =', endpoint);
  expect(endpoint.length).toBeGreaterThan(0);
  expect(endpoint).toMatch(/^(unix:\/\/|npipe:\/\/|tcp:\/\/|tcps:\/\/)/);

  // All three endpoint kinds must be offered.
  await expect(page.getByRole('button', { name: /Test connection/i })).toBeVisible();
  await expect(page.locator('#daemon-address')).toBeVisible();

  // The root-on-host warning must be on the page unconditionally.
  await expect(page.getByText('A Docker daemon is effectively root on its host')).toBeVisible();

  const errorBar = page.locator('#blazor-error-ui');
  if (await errorBar.count()) {
    await expect(errorBar).toBeHidden();
  }

  // The active-daemon tiles must not collide: a long unix:// path spilling into the next column
  // reads as one garbled value.
  const a = await page.locator('#active-daemon-endpoint').boundingBox();
  const b = await page.locator('#active-daemon-version').boundingBox();
  expect(a).not.toBeNull();
  expect(b).not.toBeNull();
  const overlaps = a!.x + a!.width > b!.x && a!.y < b!.y + b!.height && b!.y < a!.y + a!.height;
  expect(overlaps, 'active endpoint overlaps daemon version').toBeFalsy();

  const overflow = await page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow, 'horizontal overflow @1280').toBeLessThanOrEqual(0);

  await shoot(page, 'req-ui-042-daemon-1280');
});

test('testing an unreachable daemon reports the failure honestly', async ({ page }) => {
  await openAdmin(page, 1280);

  // Choose "Network host" and point it at a port nothing listens on.
  await page.locator('[aria-label="Docker daemon endpoint kind"]').click();
  await page.getByRole('option', { name: 'Network host' }).click();
  await page.locator('#daemon-address').fill('127.0.0.1:2377');
  await page.locator('#daemon-address').blur();

  await page.getByRole('button', { name: /Test connection/i }).click();

  const error = page.locator('#daemon-error');
  // The card already shows the result for the ACTIVE daemon, so wait for the report to swing to
  // the endpoint we asked it to probe rather than reading the previous line.
  await expect(error).toContainText('tcp://127.0.0.1:2377', { timeout: 30000 });

  const text = (await error.innerText()).trim();
  console.log('HONEST FAILURE =', text);

  expect(text.length).toBeGreaterThan(20);
  // Names the endpoint that was actually tried.
  expect(text).toContain('tcp://127.0.0.1:2377');
  // Says what really happened, not something unrelated.
  expect(text).toMatch(/refused|unreachable|timed out|could not|not resolve/i);
  // The class of bug this guards: a transport outage described as a missing domain object.
  expect(text.toLowerCase()).not.toContain('workspace');
  expect(text.toLowerCase()).not.toContain('collection does not exist');

  await shoot(page, 'req-ui-042-honest-failure-1280');
});

test('plain TCP endpoint warns before it is used', async ({ page }) => {
  await openAdmin(page, 1280);

  await page.locator('[aria-label="Docker daemon endpoint kind"]').click();
  await page.getByRole('option', { name: 'Network host' }).click();
  await page.locator('#daemon-address').fill('127.0.0.1:2377');
  await page.locator('#daemon-address').blur();

  await page.getByRole('button', { name: /Use this daemon/i }).click();

  const warning = page.locator('#daemon-security-warning');
  await expect(warning).toBeVisible({ timeout: 30000 });
  const text = (await warning.innerText()).trim();
  console.log('SECURITY WARNING =', text);
  expect(text).toMatch(/root on its host|plain, unauthenticated TCP/i);

  // The active endpoint now reflects the daemon that was actually selected.
  await expect(page.locator('#active-daemon-endpoint')).toHaveText('tcp://127.0.0.1:2377');
  // And its failure is still reported honestly.
  await expect(page.locator('#daemon-error')).toContainText('tcp://127.0.0.1:2377');

  await shoot(page, 'req-ui-042-plain-tcp-warning-1280');

  // Put it back to the local socket so the persisted setting does not leak into other runs.
  await page.locator('[aria-label="Docker daemon endpoint kind"]').click();
  await page.getByRole('option', { name: 'Local socket' }).click();
  await page.getByRole('button', { name: /Use this daemon/i }).click();
  await expect(page.locator('#active-daemon-endpoint')).not.toHaveText('tcp://127.0.0.1:2377', { timeout: 30000 });
});

test('daemon card renders clean at a narrow width', async ({ page }) => {
  await openAdmin(page, 430);

  await expect(page.locator('#active-daemon-endpoint')).toBeVisible();
  await expect(page.getByRole('button', { name: /Test connection/i })).toBeVisible();

  const overflow = await page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow, 'horizontal overflow @430').toBeLessThanOrEqual(0);

  await shoot(page, 'req-ui-042-daemon-430');
});
