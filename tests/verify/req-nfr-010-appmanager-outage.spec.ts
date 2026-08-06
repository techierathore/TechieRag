import { test, expect, Page } from '@playwright/test';
import { execFileSync, spawn } from 'child_process';

// REQ-NFR-010 / BRD-101 (with REQ-FN-013/015) — AppManager outage must degrade into the cached
// last-known-good license, and must degrade further once the grace window expires.
//
// The app under test points at a plain TCP forwarder instead of AppManager directly, so the
// outage can be created mid-session by killing the forwarder. Session tokens live in a
// per-circuit SessionTokenStore and are never written to browser storage, so the whole scenario
// has to run inside ONE circuit: after login, navigation happens through in-app links only —
// a page.goto() would start a new circuit and silently sign the user out.

const BASE = process.env.TD_BASE ?? 'http://localhost:5133';
const SCRATCH = '/private/tmp/claude-501/-Volumes-MacD-MyCode-TechieRag/'
  + '6ec44dff-2137-4039-9b2c-3815d9055a6d/scratchpad';
const APP_DB = '/Volumes/MacD/MyCode/TechieRag/apps/TechieDesk/data/techiedesk.db';
const SHOT = 'test-results/screens';

// Documented AppManager test user (docs/TechieDesk-UsageGuide.md section 4).
const EMAIL = process.env.TD_EMAIL ?? 'admin@appmanager.local';
const PASSWORD = process.env.TD_PASSWORD ?? 'Admin@123!';

// LicenseRevalidationMinutes is set to 1 for this run, so a minute of wall clock makes the
// in-memory status stale and forces a real re-validation attempt.
const REVALIDATION_WAIT_MS = 65000;

function sql(statement: string): string {
  return execFileSync('sqlite3', [APP_DB, statement], { encoding: 'utf8' }).trim();
}

function stopProxy() {
  try {
    execFileSync('pkill', ['-f', 'tcpproxy.js']);
  } catch {
    // pkill exits non-zero when nothing matched — that is fine.
  }
}

function startProxy() {
  const child = spawn('node', [`${SCRATCH}/tcpproxy.js`, '5199', '192.168.1.14', '5101'], {
    detached: true, stdio: 'ignore',
  });
  child.unref();
}

async function gotoInCircuit(page: Page, linkName: string, expectText: RegExp | string) {
  await page.getByRole('link', { name: linkName }).click();
  await expect(page.getByText(expectText).first()).toBeVisible({ timeout: 30000 });
}

test.describe('REQ-NFR-010 AppManager outage grace', () => {
  test.setTimeout(600000);

  test('live license -> cached on outage -> degraded after grace', async ({ page }) => {
    // ---------- 1. Sign in while AppManager is reachable ----------
    startProxy();
    await page.waitForTimeout(1500);

    await page.goto(`${BASE}/login`);
    await page.waitForFunction(() => (window as any).Blazor !== undefined, null, { timeout: 30000 });
    await page.locator('#login-email').fill(EMAIL);
    await page.locator('#login-password').fill(PASSWORD);
    await page.getByRole('button', { name: /Sign in/i }).click();
    await expect(page.getByRole('link', { name: 'Profile' })).toBeVisible({ timeout: 60000 });
    console.log('OUTAGE signed in as', EMAIL);

    // ---------- 2. Confirm a live license and a persisted last-known-good cache row ----------
    await gotoInCircuit(page, 'Profile', /License/);
    const liveCard = await page.locator('text=License').first()
      .locator('xpath=ancestor::*[contains(@class,"rounded")][1]').innerText()
      .catch(async () => await page.locator('body').innerText());
    console.log('OUTAGE license card (AppManager up):', liveCard.replace(/\s+/g, ' ').slice(0, 300));

    const cachedRows = sql('select count(*) from LicenseCache;');
    const validatedAt = sql('select ValidatedAt from LicenseCache order by ValidatedAt desc limit 1;');
    console.log(`OUTAGE LicenseCache rows=${cachedRows} lastValidatedAt=${validatedAt}`);
    expect(Number(cachedRows), 'AppManager validation should persist a last-known-good license')
      .toBeGreaterThan(0);

    // No cached-license banner while AppManager is reachable.
    await expect(page.getByText('Running on cached license')).toHaveCount(0);
    await page.screenshot({ path: `${SHOT}/req-nfr-010-license-live.png`, fullPage: true });

    // ---------- 3. Cut AppManager off mid-session ----------
    stopProxy();
    console.log('OUTAGE AppManager forwarder killed — AppManager is now unreachable');
    await page.waitForTimeout(REVALIDATION_WAIT_MS);

    // Re-enter the Profile page inside the SAME circuit: the license card re-runs
    // EnsureFreshAsync, which is now stale, tries AppManager, fails, and falls back to cache.
    await gotoInCircuit(page, 'Home', /TechieDesk|Workspaces|Welcome/i);
    await page.getByRole('link', { name: 'Profile' }).click();

    await expect(page.getByText('Running on cached license')).toBeVisible({ timeout: 60000 });
    await expect(page.getByText('Cached', { exact: true })).toBeVisible();
    await page.screenshot({ path: `${SHOT}/req-nfr-010-license-cached.png`, fullPage: true });
    console.log('OUTAGE cached-license state rendered');

    // ---------- 4. Age the cached license past the grace window ----------
    sql("update LicenseCache set ValidatedAt = datetime('now', '-100 hours');");
    console.log('OUTAGE cached license aged to 100h (grace window is 72h)');
    await page.waitForTimeout(REVALIDATION_WAIT_MS);

    await gotoInCircuit(page, 'Home', /TechieDesk|Workspaces|Welcome/i);
    await page.getByRole('link', { name: 'Profile' }).click();

    await expect(page.getByText('License verification unavailable')).toBeVisible({ timeout: 60000 });
    await expect(page.getByText(/grace period/)).toBeVisible();
    await page.screenshot({ path: `${SHOT}/req-nfr-010-license-grace-expired.png`, fullPage: true });
    console.log('OUTAGE grace-expired state rendered');

    // ---------- 5. The app itself stayed usable throughout ----------
    await gotoInCircuit(page, 'Home', /TechieDesk|Workspaces|Welcome/i);
    await expect(page.locator('#components-reconnect-modal')).toBeHidden();
  });

  test.afterAll(() => {
    // Restore a reachable AppManager for anything that runs after this spec.
    startProxy();
  });
});
