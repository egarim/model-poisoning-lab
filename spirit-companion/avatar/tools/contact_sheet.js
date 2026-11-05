// Renders every spec in three moods into PNGs and reports page errors.
// node tools/contact_sheet.js daemon.html <outdir>
const { chromium } = require('playwright');
const fs = require('fs');
(async () => {
  const [file, out] = process.argv.slice(2);
  const b = await chromium.launch(); const p = await b.newPage({ viewport: { width: 1100, height: 900 } });
  const errs = []; p.on('pageerror', e => errs.push(e.message)); p.on('console', m => m.type() === 'error' && errs.push(m.text()));
  await p.setContent('<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head><body>' + fs.readFileSync(file, 'utf8') + '</body></html>');
  await p.waitForTimeout(500);
  const archs = ['keeper','trickster','seer','wild','healer','shadow','wanderer','flame'];
  const moods = { neutral: [0.2, 0.2], happy: [0.95, 0.05], worried: [0.05, 0.95] };
  const list = [];
  for (const form of ['latam', 'slavic']) for (const [i, a] of archs.entries()) for (const [mood, [A, I]] of Object.entries(moods)) {
    await p.selectOption('#form', form); await p.selectOption('#arch', String(i));
    await p.$eval('#stage', e => { e.value = '4'; e.dispatchEvent(new Event('input')); });
    await p.evaluate(([A, I]) => { const s = window.__daemon; s.restA = A; s.restI = I; s.A = A; s.I = I; }, [A, I]);
    await p.waitForTimeout(350);
    const name = `${a}-${form}-${mood}.png`;
    await (await p.$('canvas')).screenshot({ path: `${out}/${name}` }); list.push(name);
  }
  // stage growth strip for one creature
  await p.evaluate(() => { const s = window.__daemon; s.restA = s.A = 0.3; s.restI = s.I = 0.2; });
  for (const st of [1,2,3,4,5]) { await p.$eval('#stage', (e, v) => { e.value = v; e.dispatchEvent(new Event('input')); }, String(st)); await p.waitForTimeout(300); await (await p.$('canvas')).screenshot({ path: `${out}/stage${st}.png` }); }
  console.log(JSON.stringify({ shots: list.length, errors: errs }));
  await b.close();
})();
