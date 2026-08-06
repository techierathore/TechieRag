// Verifier render (§4a) + visual-truth (§4b) sweep for `*verify all TechieDesk`.
// Offline single-user Admin boot on http://localhost:5099. One test per route; each loads at
// desktop (1280x800) and mobile (390x844), records console/Blazor errors, blank-body, horizontal
// overflow, and captures a full-page screenshot for visual inspection.
import { test, expect } from '@playwright/test';
import * as fs from 'fs';

const BASE = 'http://localhost:5099';
const OUT = 'test-results/sweep';
fs.mkdirSync(OUT, { recursive: true });

const ROUTES: { path: string; id: string; needText?: string }[] = [
  { path: '/', id: 'home' },
  { path: '/qdrant-admin', id: 'qdrant-admin' },
  { path: '/settings', id: 'settings' },
  { path: '/token-usage', id: 'token-usage' },
  { path: '/llm-settings', id: 'llm-settings' },
  { path: '/chat', id: 'chat' },
  { path: '/ingestion', id: 'ingestion' },
  { path: '/text-ingestion', id: 'text-ingestion' },
  { path: '/llm-playground', id: 'llm-playground' },
  { path: '/tool-demo', id: 'tool-demo' },
  { path: '/login', id: 'login' },
  { path: '/register', id: 'register' },
  { path: '/forgot-password', id: 'forgot-password' },
  { path: '/reset-password', id: 'reset-password' },
  { path: '/profile', id: 'profile' },
  { path: '/pricing', id: 'pricing' },
  { path: '/setup', id: 'setup' },
  { path: '/workspace/default', id: 'workspace-chat' },
  { path: '/workspace/default/settings', id: 'workspace-settings' },
  { path: '/workspace/default/documents', id: 'workspace-documents' },
];

const WIDTHS = [
  { w: 1280, h: 800, tag: 'desktop' },
  { w: 390, h: 844, tag: 'mobile' },
];

for (const route of ROUTES) {
  test(`sweep ${route.id} (${route.path})`, async ({ page }) => {
    const findings: string[] = [];
    for (const vp of WIDTHS) {
      await page.setViewportSize({ width: vp.w, height: vp.h });
      const consoleErrors: string[] = [];
      const pageErrors: string[] = [];
      page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text()); });
      page.on('pageerror', (e) => pageErrors.push(e.message));

      const resp = await page.goto(BASE + route.path, { waitUntil: 'networkidle', timeout: 30000 }).catch(() => null);
      // Give Blazor Server circuit time to render interactive content.
      await page.waitForTimeout(1200);

      const status = resp ? resp.status() : 0;
      const shot = `${OUT}/${route.id}-${vp.tag}.png`;
      await page.screenshot({ path: shot, fullPage: true }).catch(() => {});

      // Blank-body check: meaningful visible text length.
      const bodyText = (await page.locator('body').innerText().catch(() => '')).trim();
      // Horizontal overflow (§4b / REQ-NFR-006): scrollWidth must not exceed viewport.
      const metrics = await page.evaluate(() => ({
        sw: document.documentElement.scrollWidth,
        cw: document.documentElement.clientWidth,
      }));
      // Blazor error UI ("An unhandled error has occurred").
      const blazorErr = await page.locator('#blazor-error-ui').isVisible().catch(() => false);

      if (status !== 200) findings.push(`[${vp.tag}] HTTP ${status}`);
      if (bodyText.length < 30) findings.push(`[${vp.tag}] BLANK/near-empty body (len=${bodyText.length})`);
      if (metrics.sw - metrics.cw > 2) findings.push(`[${vp.tag}] H-OVERFLOW scrollWidth=${metrics.sw} vs ${metrics.cw}`);
      if (blazorErr) findings.push(`[${vp.tag}] BLAZOR-ERROR-UI visible`);
      if (pageErrors.length) findings.push(`[${vp.tag}] pageerror: ${pageErrors.slice(0, 2).join(' | ')}`);
      // Filter benign console noise (favicon, signalr negotiate 404 on prod static, etc.)
      // Filter benign noise + the documented TR-002 scoped-css 404 (TechieDesk.styles.css, known upstream issue).
      const realCon = consoleErrors.filter((e) => !/favicon|manifest|net::ERR_ABORTED.*\.map|TechieDesk\.styles\.css|the server responded with a status of 404/i.test(e));
      if (realCon.length) findings.push(`[${vp.tag}] console: ${realCon.slice(0, 2).join(' | ')}`);

      page.removeAllListeners('console');
      page.removeAllListeners('pageerror');
    }
    if (findings.length) {
      console.log(`FINDINGS ${route.id}: ${findings.join(' ;; ')}`);
    }
    expect(findings, `${route.id}: ${findings.join(' ;; ')}`).toEqual([]);
  });
}
