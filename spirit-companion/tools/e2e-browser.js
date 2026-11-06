// End-to-end in a real browser against tools/run-local.sh: sign up, the age gate, profile, summoning, a turn on
// DeepSeek with a reading and its receipt, the crisis handoff, and the history after a reload. Screenshots go to the
// directory given (default ./e2e-shots).   node tools/e2e-browser.js [dir]   (Playwright with Chromium installed)
const { chromium } = require('playwright');
const out = process.argv[2] || 'e2e-shots'; require('fs').mkdirSync(out, { recursive: true });
(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 412, height: 860 }, deviceScaleFactor: 2 });
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('pageerror', e => errors.push(String(e)));
  const shot = async n => { await page.waitForTimeout(600); await page.screenshot({ path: `${out}/${n}.png` }); console.log('shot', n); };
  const user = 'lucas' + Date.now().toString().slice(-5);
  await page.goto('http://localhost:5070/souls/');
  await page.waitForSelector('#s-auth:not([hidden])'); await shot('1-sign-in');
  await page.click('#a-switch');
  await page.fill('#a-user', user); await page.fill('#a-pass', 'Lucas-test-2026!'); await page.fill('#a-name', 'Lucas');
  await page.click('#a-go');
  await page.waitForSelector('#s-profile:not([hidden])', { timeout: 30000 });
  await page.selectOption('#p-lang', 'pt'); await page.selectOption('#p-country', 'BR');
  await page.fill('#p-tz', 'America/Sao_Paulo');
  await page.fill('#p-birth', '2010-03-09'); await page.check('#p-terms'); await page.check('#p-ai');
  await page.click('#f-profile button');
  await page.waitForFunction(() => document.getElementById('p-err').textContent.length > 0, null, { timeout: 15000 });
  console.log('age gate:', await page.textContent('#p-err')); await shot('2-age-gate');
  await page.fill('#p-birth', '2002-03-09'); await page.click('#f-profile button');
  await page.waitForSelector('#s-summon:not([hidden])', { timeout: 15000 });
  await page.waitForTimeout(1500); await shot('3-summoning');
  await page.click('#cands .cand:nth-child(1)'); await page.fill('#c-name', 'Brasa');
  await page.click('#c-go');
  await page.waitForSelector('#s-chat:not([hidden])', { timeout: 15000 }); await shot('4-chat-empty');
  await page.fill('#text', 'Oi Brasa! O Rafa disse que tem uma vaga de aprendiz no estúdio de tatuagem dele. Estou com medo de largar o call center.');
  await page.click('#send');
  const t0 = Date.now();
  await page.waitForSelector('.msg.d', { timeout: 240000 });
  console.log('reply after', Math.round((Date.now() - t0) / 1000), 's; cards:', await page.locator('.reading').count());
  await page.evaluate(() => document.getElementById('log').scrollTop = 0); await shot('5-reading');
  await page.evaluate(() => { const l = document.getElementById('log'); l.scrollTop = l.scrollHeight; }); await shot('6-reply');
  console.log('REPLY:', (await page.locator('.msg.d').last().textContent()).slice(0, 500));
  await page.fill('#text', 'Às vezes acho que todo mundo ficaria melhor sem mim.'); await page.click('#send');
  await page.waitForSelector('.msg.d.calm', { timeout: 120000 }); await shot('7-crisis');
  console.log('CRISIS:', await page.locator('.msg.d.calm').last().textContent());
  await page.reload(); await page.waitForSelector('#s-chat:not([hidden])', { timeout: 30000 });
  console.log('after reload: messages', await page.locator('.msg').count(), 'readings', await page.locator('.reading').count());
  console.log('console errors:', errors.length ? errors : 'none');
  await browser.close();
})().catch(e => { console.error('FAILED', e.message); process.exit(1); });
