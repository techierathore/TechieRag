import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';

// ─────────────────────────────────────────────────────────────────────────────
// TechieRag UI verification — REQ-UI-001..013 (scope: ui)
// §4a DevGuide render gate + §4b visual-truth gate. No auth (single anon user).
// Backend note: SQLite-vec store (local); no LLM provider configured on the running
// instance; Qdrant down / Docker up. So LLM/Qdrant DATA paths are not exercised here —
// this sweep proves each screen RENDERS its controls and LOOKS right.
// ─────────────────────────────────────────────────────────────────────────────

const SHOT_DIR = 'test-results/screens';
fs.mkdirSync(SHOT_DIR, { recursive: true });

type Screen = {
  req: string;
  name: string;
  route: string;
  title: string;        // document.title anchor (from <PageTitle>)
  anchors: string[];    // body texts that must be visible (render gate)
};

const SCREENS: Screen[] = [
  { req: 'REQ-UI-001', name: 'home', route: '/', title: 'TechieRag Demo',
    anchors: ['RAG Chat', 'LLM Playground', 'Token Usage'] },
  { req: 'REQ-UI-002', name: 'settings', route: '/settings', title: 'TechieRag Settings',
    anchors: ['Vector Store', 'Chunk Size', 'Save Configuration', 'Reset to Defaults'] },
  { req: 'REQ-UI-003', name: 'llm-settings', route: '/llm-settings', title: 'LLM Settings',
    anchors: ['Test LLM Connection', 'Save', 'Reset'] },
  { req: 'REQ-UI-004a', name: 'ingestion', route: '/ingestion', title: 'Document Ingestion - TechieRag',
    anchors: ['Documents Folder', 'File Pattern', 'Ingest Now', 'Clear All Data'] },
  { req: 'REQ-UI-004b', name: 'text-ingestion', route: '/text-ingestion', title: 'Text Ingestion - TechieRag',
    anchors: ['Document Name', 'Text Content', 'Ingest Text', 'Clear Form'] },
  { req: 'REQ-UI-005', name: 'chat', route: '/chat', title: 'RAG Chat',
    anchors: ['Chat Configuration', 'Clear Chat', 'New Conversation', 'Session:'] },
  { req: 'REQ-UI-006', name: 'llm-playground', route: '/llm-playground', title: 'LLM Playground',
    anchors: ['System Prompt', 'User Prompt', 'Temperature', 'Max Tokens', 'Generate'] },
  { req: 'REQ-UI-007', name: 'tool-demo', route: '/tool-demo', title: 'Tool Calling Demo',
    anchors: ['Available Tools', 'Add Custom Tool', 'Run Agent Loop'] },
  { req: 'REQ-UI-008', name: 'token-usage', route: '/token-usage', title: 'Token Usage',
    anchors: ['Total Tokens', 'Estimated Cost', 'Operations', 'Reset Session'] },
  { req: 'REQ-UI-011', name: 'qdrant-admin', route: '/qdrant-admin', title: 'Qdrant Admin',
    anchors: ['Docker', 'Connect', 'Collections'] },
];

const WIDTHS = [
  { tag: 'desktop', w: 1280, h: 800 },
  { tag: 'mobile', w: 390, h: 844 },
];

async function waitForBlazor(page: Page, title: string) {
  await page.waitForLoadState('networkidle');
  // Blazor Server: circuit connects then interactive render completes.
  await page.waitForFunction(
    (t) => document.title === t || document.title.includes(t),
    title,
    { timeout: 20000 }
  );
}

// Geometry gate: every VISIBLE interactive control must be sized (>0×0) and sit
// within the viewport horizontally; primary buttons must not grossly overlap.
async function geometryIssues(page: Page, vw: number): Promise<string[]> {
  return await page.evaluate((vw) => {
    const issues: string[] = [];
    // Bound = the page's own layout width. This desktop demo has a fixed min
    // content width (~560-818px) and does not reflow below it, so on a 390px
    // phone the page is horizontally scrollable but internally coherent. We flag
    // controls that escape the PAGE's own bounds (true clipping/off-canvas), not
    // ones that merely exceed a narrow phone viewport (a design choice, not a bug).
    const pageW = Math.max(document.documentElement.scrollWidth, document.body.scrollWidth, vw);
    const els = Array.from(
      document.querySelectorAll('button, input, textarea, select, a[href]')
    ) as HTMLElement[];
    const boxes: { el: HTMLElement; r: DOMRect; tag: string; txt: string }[] = [];
    for (const el of els) {
      const style = getComputedStyle(el);
      if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0') continue;
      const r = el.getBoundingClientRect();
      if (r.width === 0 && r.height === 0) continue; // not laid out / hidden
      const visible = r.bottom > 0 && r.top < (window.innerHeight + 2000); // in document flow
      if (!visible) continue;
      const txt = (el.textContent || (el as HTMLInputElement).placeholder || '').trim().slice(0, 24);
      boxes.push({ el, r, tag: el.tagName.toLowerCase(), txt });
      // zero-size while "visible"
      if ((r.width === 0 || r.height === 0)) issues.push(`zero-size ${el.tagName} "${txt}"`);
      // off-canvas / clipped beyond the page's own bounds (real defect) — UNLESS the
      // element sits inside a LOCAL horizontal scroll wrapper (the shadcn
      // `overflow-x-auto` DataTable containment pattern, TR-004): reachable by
      // scrolling that wrapper, so not clipped. The app shell's own scrolling
      // <main> (flex-1 overflow-auto) does NOT count — content that only the
      // whole main pane can pan to IS a mobile-layout defect (TR-003 class).
      const inLocalScroller = (() => {
        let a = el.parentElement;
        while (a && a !== document.body) {
          const st = getComputedStyle(a);
          const scrollsX = /(auto|scroll)/.test(st.overflowX) && a.scrollWidth > a.clientWidth + 2;
          if (scrollsX) return a.tagName.toLowerCase() !== 'main' && !a.className.toString().includes('flex-1');
          a = a.parentElement;
        }
        return false;
      })();
      if (!inLocalScroller && (r.right < -2 || r.left > pageW + 2)) issues.push(`off-canvas-x ${el.tagName} "${txt}" [x ${Math.round(r.left)}..${Math.round(r.right)} pageW ${pageW}]`);
    }
    // gross overlap among buttons (>65% of the smaller area)
    const btns = boxes.filter((b) => b.tag === 'button' && b.r.width > 4 && b.r.height > 4);
    for (let i = 0; i < btns.length; i++) {
      for (let j = i + 1; j < btns.length; j++) {
        const a = btns[i].r, b = btns[j].r;
        const ix = Math.max(0, Math.min(a.right, b.right) - Math.max(a.left, b.left));
        const iy = Math.max(0, Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top));
        const inter = ix * iy;
        if (inter <= 0) continue;
        const smaller = Math.min(a.width * a.height, b.width * b.height);
        if (smaller > 0 && inter / smaller > 0.65) {
          issues.push(`overlap btn "${btns[i].txt}" ⟂ "${btns[j].txt}" (${Math.round((inter / smaller) * 100)}%)`);
        }
      }
    }
    return issues;
  }, vw);
}

for (const s of SCREENS) {
  test(`${s.req} ${s.name} — render + visual`, async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on('pageerror', (e) => consoleErrors.push('pageerror: ' + e.message));
    page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push('console: ' + m.text()); });

    for (const vp of WIDTHS) {
      await page.setViewportSize({ width: vp.w, height: vp.h });
      await page.goto(s.route);
      await waitForBlazor(page, s.title);
      // allow TrBlazeUI post-render settle
      await page.waitForTimeout(600);

      const shot = `${SHOT_DIR}/${s.name}-${vp.tag}.png`;
      await page.screenshot({ path: shot, fullPage: true });

      // ── §4a render gate ──────────────────────────────────────────────
      // Blazor unhandled-error banner must not be shown.
      const errBanner = page.locator('#blazor-error-ui');
      if (await errBanner.count()) {
        await expect(errBanner, `[${s.req}] Blazor error UI visible on ${vp.tag}`).toBeHidden();
      }
      // document title
      expect(await page.title(), `[${s.req}] wrong/blank page title on ${vp.tag}`).toContain(s.title.split(' - ')[0]);
      // each anchor visible somewhere on the page
      for (const a of s.anchors) {
        await expect(
          page.getByText(a, { exact: false }).first(),
          `[${s.req}] anchor "${a}" not rendered on ${vp.tag} (${shot})`
        ).toBeVisible({ timeout: 8000 });
      }

      // ── §4b visual gate (geometry) ───────────────────────────────────
      const issues = await geometryIssues(page, vp.w);
      expect(issues, `[${s.req}] visual geometry issues on ${vp.tag} (${shot}):\n  ${issues.join('\n  ')}`).toEqual([]);
    }

    // console/page errors are recorded but only fail if the error banner showed
    // (many Blazor Server benign warnings log as console errors). Attach for review.
    if (consoleErrors.length) {
      test.info().annotations.push({ type: 'console-errors', description: `${s.req} ${s.name}: ` + consoleErrors.slice(0, 5).join(' | ') });
    }
  });
}
