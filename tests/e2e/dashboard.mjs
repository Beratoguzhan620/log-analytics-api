// The dashboard as a person uses it, against a running stack with the sample generator:
//   docker compose up --build -d && npm i playwright && node dashboard.mjs
// Screenshots of every step go to ./output.
import { chromium } from 'playwright';
import { mkdir } from 'node:fs/promises';

const base = process.env.BASE_URL ?? 'http://localhost:8080';
const email = process.env.ADMIN_EMAIL ?? 'admin@example.com';
const password = process.env.ADMIN_PASSWORD ?? 'admin-password-local';
const out = new URL('./output/', import.meta.url).pathname;
await mkdir(out, { recursive: true });

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
const problems = [];
page.on('pageerror', (error) => problems.push(`page error: ${error.message}`));
page.on('console', (message) => { if (message.type() === 'error' && !message.text().includes('401')) problems.push(`console: ${message.text()}`); });

let failures = 0;
async function step(name, action) {
  try {
    await action();
    console.log(`ok   ${name}`);
  } catch (error) {
    failures++;
    console.log(`FAIL ${name}\n     ${error.message.split('\n')[0]}`);
    await page.screenshot({ path: `${out}fail-${name.replace(/\W+/g, '-')}.png` }).catch(() => {});
  }
}

function expect(condition, message) {
  if (!condition) throw new Error(message);
}

const shot = (name) => page.screenshot({ path: `${out}${name}.png` });

await step('the sign-in page comes first', async () => {
  await page.goto(base);
  await page.getByRole('heading', { name: 'Sign in' }).waitFor();
  await shot('01-sign-in');
});

await step('a wrong password is refused', async () => {
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill('not-the-password');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.getByRole('alert').filter({ hasText: 'incorrect' }).waitFor();
});

await step('signing in opens the overview with the sample traffic', async () => {
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.getByRole('heading', { name: 'Overview' }).waitFor();
  await page.locator('.kpi .value').first().waitFor();
  await page.locator('.chart svg rect').first().waitFor();
  await page.locator('.error-item').first().waitFor({ timeout: 20000 });
  const events = await page.locator('.kpi .value').first().textContent();
  expect(events && events !== '0', `expected events, got ${events}`);
  await page.waitForTimeout(500);
  await shot('02-overview');
});

await step('a chart bar shows its counts', async () => {
  const bars = page.locator('.chart g.bar');
  await bars.nth(Math.floor((await bars.count()) / 2)).hover();
  await page.locator('.tooltip:not([hidden])').waitFor();
});

await step('the time range can be changed', async () => {
  await page.getByRole('button', { name: '7d' }).click();
  await page.waitForURL(/range=7d/);
  await page.locator('.kpi .value').first().waitFor();
  await page.getByRole('button', { name: '24h' }).click();
  await page.waitForURL(/range=24h/);
});

await step('a frequent error leads to its events', async () => {
  await page.locator('.error-item').first().click();
  await page.getByRole('heading', { name: 'Search' }).waitFor();
  await page.locator('.event-row').first().waitFor();
  expect(await page.locator('#level').inputValue() === 'Error', 'level filter should be Error');
  const badges = await page.locator('.event-row .level').allTextContents();
  expect(badges.every((b) => b === 'ERR' || b === 'FTL'), `only errors expected, got ${badges.slice(0, 5)}`);
});

await step('an event opens with its exception and properties', async () => {
  await page.locator('.event-row').first().click();
  await page.locator('.detail:not([hidden]) .props').first().waitFor();
  await shot('03-search-event');
});

await step('its trace shows the whole order across services', async () => {
  await page.locator('.detail:not([hidden])').getByRole('link', { name: 'View trace' }).click();
  await page.getByRole('heading', { name: 'Trace' }).waitFor();
  await page.locator('.timeline li').first().waitFor();
  const services = new Set(await page.locator('.timeline .chip').allTextContents());
  expect(services.size >= 2, `expected several services, got ${[...services]}`);
  await shot('04-trace');
});

await step('full-text search with a property filter finds one order', async () => {
  await page.goto(`${base}/#/search?range=24h`);
  await page.locator('.event-row').first().waitFor();
  const message = await page.locator('.event-row .msg').filter({ hasText: 'placed by customer' }).first().textContent();
  const order = message.match(/SIP-\d+/)[0];
  await page.getByLabel('Text').fill('placed');
  await page.getByLabel('Property').fill(`OrderId:${order}`);
  await page.getByRole('button', { name: 'Search' }).click();
  await page.waitForURL(/prop=/);
  // The previous results stay on screen until the new ones arrive, so wait for the list to shrink.
  await page.waitForFunction(() => document.querySelectorAll('.event-row').length === 1, null, { timeout: 15000 }).catch(() => {});
  const rows = await page.locator('.event-row .msg').allTextContents();
  expect(rows.length === 1 && rows[0].includes(order), `expected one row for ${order}, got ${rows.length}`);
});

await step('a search with no matches says so', async () => {
  await page.getByLabel('Text').fill('no-such-word-anywhere-xyz');
  await page.getByLabel('Property').fill('');
  await page.getByRole('button', { name: 'Search' }).click();
  await page.getByText('No events match').waitFor();
});

await step('an admin adds an alert rule', async () => {
  await page.getByRole('link', { name: 'Alerts' }).click();
  await page.getByRole('heading', { name: 'New rule' }).waitFor();
  await page.getByLabel('Name').fill(`Payment errors ${Date.now()}`);
  await page.getByLabel('Service').fill('payments');
  await page.getByLabel('At least … events').fill('5');
  await page.getByRole('button', { name: 'Add rule' }).click();
  await page.locator('table td', { hasText: '≥ 5 × Error+ in 5 min · payments' }).first().waitFor();
  await shot('05-alerts');
});

await step('an admin adds a source and sees its key once', async () => {
  await page.getByRole('link', { name: 'Sources' }).click();
  await page.getByRole('heading', { name: 'Add a source' }).waitFor();
  await page.getByLabel('Name').fill(`Checkout ${Date.now()}`);
  await page.getByRole('button', { name: 'Add source' }).click();
  const key = await page.locator('.secret code').textContent();
  expect(/^la_[a-z0-9]{8}_/.test(key), `unexpected key ${key}`);
  await shot('06-sources');
});

await step('the layout holds on a phone', async () => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('link', { name: 'Overview' }).click();
  await page.locator('.chart svg rect').first().waitFor();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(overflow <= 1, `page scrolls sideways by ${overflow}px`);
  await shot('07-phone');
  await page.setViewportSize({ width: 1280, height: 800 });
});

await step('signing out returns to the sign-in page', async () => {
  await page.getByRole('button', { name: 'Sign out' }).click();
  await page.getByRole('heading', { name: 'Sign in' }).waitFor();
});

await step('no script errors along the way', async () => {
  expect(problems.length === 0, problems.join(' | '));
});

await browser.close();
console.log(failures === 0 ? '\nall steps passed' : `\n${failures} step(s) failed`);
process.exit(failures === 0 ? 0 : 1);
