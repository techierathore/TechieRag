import { test, expect } from '@playwright/test';

// §4b visual-truth gate — mobile overflow standard set 2026-07-02 (TR-003/TR-004 fixes):
// a page must not stretch the document beyond the 390px viewport on fresh load.
// scrollWidth > viewport means content clips off-canvas on a phone.

const ROUTES = [
  { req: 'REQ-UI-001', route: '/', title: 'TechieRag Demo' },
  { req: 'REQ-UI-002', route: '/settings', title: 'TechieRag Settings' },
  { req: 'REQ-UI-003', route: '/llm-settings', title: 'LLM Settings' },
  { req: 'REQ-UI-004', route: '/ingestion', title: 'Document Ingestion' },
  { req: 'REQ-UI-004', route: '/text-ingestion', title: 'Text Ingestion' },
  { req: 'REQ-UI-005', route: '/chat', title: 'RAG Chat' },
  { req: 'REQ-UI-006', route: '/llm-playground', title: 'LLM Playground' },
  { req: 'REQ-UI-007', route: '/tool-demo', title: 'Tool Calling Demo' },
  { req: 'REQ-UI-008', route: '/token-usage', title: 'Token Usage' },
  { req: 'REQ-UI-011', route: '/qdrant-admin', title: 'Qdrant Admin' },
];

test.use({ viewport: { width: 390, height: 844 } });

for (const r of ROUTES) {
  test(`${r.req} mobile overflow gate ${r.route} — scrollWidth fits 390px`, async ({ page }) => {
    await page.goto(r.route);
    await page.waitForLoadState('networkidle');
    await page.waitForFunction(
      (t) => document.title.includes(t),
      r.title,
      { timeout: 20000 }
    );
    await page.waitForTimeout(600);
    const scrollWidth = await page.evaluate(() =>
      Math.max(document.documentElement.scrollWidth, document.body.scrollWidth)
    );
    expect(
      scrollWidth,
      `${r.req} ${r.route} overflows the 390px viewport (scrollWidth=${scrollWidth})`
    ).toBeLessThanOrEqual(395);
  });
}
