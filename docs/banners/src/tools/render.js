// node render.js <html> <out.png> <W> <H> <scale> [<html> <out.png> <W> <H> <scale> ...]
// Renders each composition at its exact CSS size and device scale (1 for @1x, 2 for @2x; heroes are 1920x1080 at 1).
// Adapted from Cargo's src/tools/render.js. NODE_PATH must reach playwright.
const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const jobs = [];
  const a = process.argv.slice(2);
  for (let i = 0; i < a.length; i += 5) jobs.push(a.slice(i, i + 5));
  const b = await chromium.launch();
  for (const [html, out, W, H, s] of jobs) {
    const ctx = await b.newContext({ viewport: { width: +W, height: +H }, deviceScaleFactor: +s });
    const p = await ctx.newPage();
    await p.goto('file://' + path.resolve(html));
    await p.evaluate(() => document.fonts.ready);
    await p.waitForFunction(() => [...document.images].every(i => i.complete && i.naturalWidth > 0), null, { timeout: 30000 });
    await p.waitForTimeout(300);
    await p.screenshot({ path: out, clip: { x: 0, y: 0, width: +W, height: +H } });
    await ctx.close();
    console.log('rendered', out);
  }
  await b.close();
})();
