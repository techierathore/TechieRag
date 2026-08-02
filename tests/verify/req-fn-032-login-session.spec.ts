import { test, expect, Page } from '@playwright/test';

test.setTimeout(120000);

/**
 * REQ-FN-032 (session continuity across circuits) + REQ-UI-007 (login screen & flow).
 *
 * Acceptance under test, verbatim:
 *   "a user who authenticates successfully against AppManager lands on the requested route in an
 *    authenticated session, and a page refresh does not sign them out."
 *   "valid creds land on last-requested route; INVALID_CREDENTIALS shows friendly error."
 *
 * Run against a live app booted with ASPNETCORE_ENVIRONMENT=Development on :5099 in AppManager
 * mode. Includes the §4b visual-truth gate at 1280x800 and 390x844.
 */

const EMAIL = 'admin@appmanager.local';
const PASSWORD = 'Admin@123!';

async function signIn(page: Page, deepLink: string) {
  await page.goto(deepLink);
  // The route guard must bounce an anonymous visitor to /login with the deep link preserved.
  await page.waitForURL(/\/login\?returnUrl=/, { timeout: 30000 });
  await page.fill('#login-email', EMAIL);
  await page.fill('#login-password', PASSWORD);
  await page.click('button[type="submit"]');
}

async function expectAuthenticatedShell(page: Page) {
  // The shell only renders for an authenticated visitor; an anonymous one is redirected away.
  await expect(page.getByText('User menu')).toBeAttached({ timeout: 30000 });
  expect(page.url(), 'visitor was bounced back to /login — the session did not survive')
    .not.toContain('/login');
}

async function assertNoOverflow(page: Page, width: number) {
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
    return Array.from(document.querySelectorAll('h1, input, button, a'))
      .filter(e => (e as HTMLElement).offsetParent !== null)
      // The WCAG 2.4.1 skip link is parked off-canvas by design until it takes focus.
      .filter(e => !e.classList.contains('td-skip-link'))
      .map(e => {
        const r = e.getBoundingClientRect();
        return {
          tag: e.tagName,
          text: (e.textContent || '').slice(0, 30),
          x: r.x, y: r.y, w: r.width, h: r.height,
          scrollable: inScrollContainer(e),
        };
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
}

test('REQ-UI-007 deep link while signed out lands on /login with returnUrl preserved', async ({ page }) => {
  await page.goto('/settings');
  await page.waitForURL(/\/login\?returnUrl=%2Fsettings/, { timeout: 30000 });
  await expect(page.getByText('Welcome back')).toBeVisible();
  await expect(page.locator('#login-email')).toBeVisible();
  await expect(page.locator('#login-password')).toBeVisible();
});

test('REQ-UI-007 login screen looks right at 1280 and 390', async ({ page }) => {
  for (const [width, height] of [[1280, 800], [390, 844]] as const) {
    await page.setViewportSize({ width, height });
    await page.goto('/login');
    await page.waitForLoadState('networkidle');
    await expect(page.getByText('Welcome back')).toBeVisible();
    await expect(page.locator('#login-email')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
    await assertNoOverflow(page, width);
    await page.screenshot({ path: `test-results/screens/req-ui-007-login-${width}.png`, fullPage: true });
  }
});

test('REQ-UI-007 INVALID_CREDENTIALS shows the friendly error and grants no session', async ({ page }) => {
  await page.goto('/login');
  await page.fill('#login-email', EMAIL);
  await page.fill('#login-password', 'definitely-the-wrong-password');
  await page.click('button[type="submit"]');

  await page.waitForURL(/\/login\?/, { timeout: 30000 });
  await expect(page.getByText('Invalid email or password.')).toBeVisible({ timeout: 20000 });

  // No session cookie may have been issued by a failed login.
  const cookies = await page.context().cookies();
  expect(cookies.find(c => c.name === 'td.sid'), 'a failed login issued a session cookie').toBeUndefined();
});

test('REQ-FN-032 login lands on the requested route AND survives a page refresh', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });

  // 1. deep link -> /login?returnUrl -> sign in -> land on the ORIGINAL route.
  await signIn(page, '/settings');
  await page.waitForURL(/\/settings$/, { timeout: 30000 });
  await expectAuthenticatedShell(page);
  await expect(page).toHaveTitle(/Settings/i);

  // The browser holds ONLY an opaque handle — no JWT, no email, no role.
  const sid = (await page.context().cookies()).find(c => c.name === 'td.sid');
  expect(sid, 'no td.sid session cookie was issued').toBeDefined();
  expect(sid!.httpOnly, 'td.sid must be HttpOnly').toBe(true);
  expect(sid!.sameSite, 'td.sid must be SameSite=Lax').toBe('Lax');
  expect(sid!.value).not.toContain(EMAIL);
  expect(sid!.value.split('.').length, 'td.sid must not be a JWT').toBeLessThan(3);

  await page.screenshot({ path: 'test-results/screens/req-fn-032-after-login-desktop.png', fullPage: true });
  await assertNoOverflow(page, 1280);

  // 2. THE acceptance clause the old code failed: a hard reload must NOT sign the user out.
  await page.reload({ waitUntil: 'load' });
  await page.waitForLoadState('networkidle');
  expect(page.url(), 'F5 signed the user out — REQ-FN-032 regression').toContain('/settings');
  await expectAuthenticatedShell(page);

  // 3. a second full-page navigation (a brand new circuit again) is still authenticated.
  await page.goto('/');
  await page.waitForLoadState('networkidle');
  await expectAuthenticatedShell(page);
  expect(page.url()).not.toContain('/login');

  // 4. §4b visual truth at mobile width, still signed in.
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(600);
  await expectAuthenticatedShell(page);
  await assertNoOverflow(page, 390);
  await page.screenshot({ path: 'test-results/screens/req-fn-032-after-login-mobile.png', fullPage: true });
});

test('REQ-UI-008 logout clears the session cookie and returns to /login', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await signIn(page, '/');
  await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 30000 });
  await expectAuthenticatedShell(page);

  await page.getByText('User menu').locator('..').click();
  await page.getByText('Log out', { exact: true }).click();

  await page.waitForURL(/\/login/, { timeout: 30000 });
  const sid = (await page.context().cookies()).find(c => c.name === 'td.sid');
  expect(sid?.value ?? '', 'the session cookie survived logout').toBe('');
});
