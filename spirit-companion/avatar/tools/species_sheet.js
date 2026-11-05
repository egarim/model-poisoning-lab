// Renders every creature (folklore + species variants) in neutral/happy/worried, plus the summoning panel.
// node tools/species_sheet.js daemon.html <outdir>
const { chromium } = require('playwright'); const fs = require('fs');
(async () => {
  const [file, out] = process.argv.slice(2);
  const b = await chromium.launch(); const p = await b.newPage({ viewport: { width: 1100, height: 1400 } });
  const errs = []; p.on('pageerror', e => errs.push(e.message)); p.on('console', m => m.type() === 'error' && errs.push(m.text()));
  await p.setContent('<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head><body>' + fs.readFileSync(file, 'utf8') + '</body></html>');
  await p.waitForTimeout(800);
  await p.screenshot({ path: `${out}/summon.png`, clip: { x: 0, y: 0, width: 1100, height: 900 } });
  const keys = await p.$$eval('#browse option', os => os.map(o => o.value).filter(Boolean).filter(v => !v.startsWith('folk:')));
  await p.$eval('#stage', e => { e.value = '4'; e.dispatchEvent(new Event('input')); });
  const names = [];
  for (const k of keys) for (const [mood, A, I] of [['neutral', .25, .2], ['happy', .95, .05], ['worried', .05, .95]]) {
    await p.evaluate(k => window.__forge.set(k), k);
    await p.evaluate(([A, I]) => { const s = window.__forge.st; s.A = s.restA = A; s.I = s.restI = I; }, [A, I]);
    await p.waitForTimeout(300);
    const n = `${k.replace(':', '-')}-${mood}.png`; await (await p.$('#cv')).screenshot({ path: `${out}/${n}` }); names.push(n);
  }
  fs.writeFileSync(`${out}/list.json`, JSON.stringify(keys));
  console.log(JSON.stringify({ creatures: keys.length, shots: names.length, errors: errs }));
  await b.close();
})();
