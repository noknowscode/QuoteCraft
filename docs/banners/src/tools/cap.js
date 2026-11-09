// QuoteCraft capture driver (adapted from Cargo's src/tools/cap.js).
//
//   node cap.js <url> <outdir> <scheme> <WxH> <dpr> <steps.json>
//
// Loads the Release WebAssembly build in headless Chromium and passes the app's own sample-data argument
// (--qc-seed=sample, App.Verification.cs) to its Main: the Uno bootstrapper calls Main with no arguments, so the
// bootstrapper script is patched in flight to hand over QC_ARGS (default "--qc-seed=sample"). Each run is a fresh
// browser profile, so the IndexedDB database starts empty and the app seeds it once, before its first page.
//
// steps: [{click:[x,y]}, {move:[x,y]}, {wheel:[dx,dy], at:[x,y]}, {key:"Escape"}, {type:"text"},
//         {scheme:"dark"}, {viewport:[w,h]}, {wait:ms}, {settle:ms},
//         {shot:"name", clip:{x,y,width,height}}]
// {settle} waits until two screenshots taken 700 ms apart are identical (animations done), at most <ms>.
// {click:[x,y], expect:[x,y,r,g,b]} then polls that pixel (CSS px) for up to 10 s and clicks again, up to 3 times,
// until the colour the next state shows is there. At DPR 3 the app sometimes drops the first click after a list or
// tab selection (the pointer stays with the selected item); the check makes the run independent of that.
const { chromium } = require('playwright');
const fs = require('fs');

async function run(url, out, scheme, size, dpr, steps, log = console.log) {
  let [w, h] = size.split('x').map(Number);
  const args = (process.env.QC_ARGS ?? '--qc-seed=sample').split(',').filter(Boolean);
  const b = await chromium.launch({ args: ['--hide-scrollbars'] });
  const ctx = await b.newContext({ viewport: { width: w, height: h }, deviceScaleFactor: Number(dpr), colorScheme: scheme });
  const p = await ctx.newPage();
  const errors = [];
  p.on('pageerror', e => errors.push(String(e)));
  p.on('console', m => { if (/VERIFY|seed/i.test(m.text())) log('app:', m.text()); });
  await p.route(/uno-bootstrap.*\.js$/, async route => {
    const res = await route.fetch();
    let body = await res.text();
    const call = 'this._runMain(this._unoConfig.uno_main, [])';
    if (!body.includes(call)) log('WARNING: bootstrapper call not found; the app will start without arguments');
    body = body.replace(call, `this._runMain(this._unoConfig.uno_main, ${JSON.stringify(args)})`);
    await route.fulfill({ response: res, body });
  });
  await p.goto(url, { waitUntil: 'load', timeout: 180000 });
  const t0 = Date.now();
  while (Date.now() - t0 < 300000) {
    let strip = null;
    try { strip = await p.screenshot({ clip: { x: 10, y: 8, width: 300, height: 40 }, scale: 'css', timeout: 120000 }); } catch (e) { log('probe timeout'); }
    if (strip && strip.length > 1500) break;
    await p.waitForTimeout(2000);
  }
  log('rendered after', Math.round((Date.now() - t0) / 1000), 's');
  // Park the pointer outside the page: no hover state on any control (steps park it again with {move:[-20,-20]}).
  await p.mouse.move(-20, -20);

  const api = {
    page: p,
    async step(s) {
      if (s.viewport) { [w, h] = s.viewport; await p.setViewportSize({ width: w, height: h }); }
      if (s.click) {
        const press = async () => { await p.mouse.move(...s.click); await p.mouse.down(); await p.waitForTimeout(100); await p.mouse.up(); };
        await press();
        if (s.expect) {
          const [ex, ey, ...rgb] = s.expect;
          const matches = async () => {
            const png = await p.screenshot({ clip: { x: ex, y: ey, width: 1, height: 1 }, scale: 'css' });
            const px = await p.evaluate(async b64 => {
              const img = new Image(); img.src = 'data:image/png;base64,' + b64; await img.decode();
              const c = new OffscreenCanvas(1, 1).getContext('2d'); c.drawImage(img, 0, 0); return [...c.getImageData(0, 0, 1, 1).data];
            }, png.toString('base64'));
            return rgb.slice(0, 3).every((v, i) => Math.abs(v - px[i]) <= (rgb[3] ?? 24));
          };
          let ok = false;
          for (let attempt = 1; attempt <= 3 && !ok; attempt++) {
            if (attempt > 1) { log('  no state change after', JSON.stringify(s.click), '- clicking again'); await press(); }
            for (const t0 = Date.now(); Date.now() - t0 < 10000;) { if (await matches()) { ok = true; break; } await p.waitForTimeout(500); }
          }
          if (!ok) log('WARNING: expected state not reached after', JSON.stringify(s.click));
        }
      }
      if (s.move) await p.mouse.move(...s.move);
      if (s.wheel) { await p.mouse.move(...(s.at || [w / 2, h / 2])); await p.mouse.wheel(...s.wheel); }
      if (s.key) await p.keyboard.press(s.key);
      if (s.type) await p.keyboard.type(s.type, { delay: 40 });
      if (s.scheme) await p.emulateMedia({ colorScheme: s.scheme });
      if (s.wait) await p.waitForTimeout(s.wait);
      if (s.settle) {
        const until = Date.now() + s.settle;
        let prev = await p.screenshot({ scale: 'css' });
        while (Date.now() < until) {
          await p.waitForTimeout(700);
          const next = await p.screenshot({ scale: 'css' });
          if (next.equals(prev)) break;
          prev = next;
        }
      }
      if (s.shot) {
        const t = Date.now();
        await p.screenshot({ path: `${out}/${s.shot}.png`, clip: s.clip, animations: 'disabled', timeout: 180000 });
        log('shot', s.shot, (Date.now() - t) + 'ms');
      }
    },
    errors,
    close: () => b.close(),
  };
  for (const s of steps) await api.step(s);
  return api;
}

module.exports = { run };

if (require.main === module) {
  (async () => {
    const [url, out, scheme, size, dpr, stepsFile] = process.argv.slice(2);
    const steps = JSON.parse(fs.readFileSync(stepsFile, 'utf8'));
    const api = await run(url, out, scheme, size, dpr, steps);
    if (api.errors.length) console.log('errors', api.errors.slice(0, 5));
    await api.close();
  })();
}
