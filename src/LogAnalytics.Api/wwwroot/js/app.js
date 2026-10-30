import { api, AuthError } from './api.js';
import { h, icon, clear, errorState } from './dom.js';
import * as views from './views.js';

const RANGES = ['15m', '1h', '24h', '7d'];
const root = document.getElementById('app');
let user = null;
let range = '24h';

const NAV = [
  ['overview', 'Overview', 'overview'],
  ['search', 'Search', 'search'],
  ['alerts', 'Alerts', 'bell'],
  ['sources', 'Sources', 'plug'],
];

function parseHash() {
  const [path, search = ''] = location.hash.replace(/^#\/?/, '').split('?');
  const [page = 'overview', id] = path.split('/');
  return { page: page || 'overview', id: id ? decodeURIComponent(id) : undefined, params: Object.fromEntries(new URLSearchParams(search)) };
}

function go(page, params = {}) {
  const clean = Object.fromEntries(Object.entries(params).filter(([, v]) => v !== undefined && v !== null && v !== ''));
  const search = new URLSearchParams(clean).toString();
  location.hash = `#/${page}${search ? `?${search}` : ''}`;
}

async function render() {
  if (!user) {
    try {
      user = await api('/api/me');
    } catch (error) {
      if (error instanceof AuthError) {
        clear(root, views.signIn((signedIn) => { user = signedIn; render(); }));
        return;
      }

      clear(root, errorState('The API did not answer. Is it running?', render));
      return;
    }
  }

  const { page, id, params } = parseHash();
  if (params.range && RANGES.includes(params.range)) range = params.range;
  const ctx = { user, range, params, traceId: id, go };
  const builder = { overview: views.overview, search: views.search, trace: views.trace, alerts: views.alerts, sources: views.sources }[page] ?? views.overview;

  let built;
  try {
    built = builder(ctx);
  } catch (error) {
    if (error instanceof AuthError) { user = null; render(); return; }
    throw error;
  }

  clear(root, h('div', { class: 'shell' }, sidebar(page), h('main', { class: 'main', id: 'main' }, topbar(built, page, params), built.view)));
  document.title = `${built.title} · Log Analytics`;
}

function sidebar(page) {
  return h('aside', { class: 'sidebar', 'aria-label': 'Main' },
    h('div', { class: 'brand' }, h('span', { class: 'brand-mark' }, icon('logo')), h('span', {}, 'Log Analytics')),
    h('nav', { class: 'nav' }, NAV.map(([key, label, iconName]) =>
      h('a', { href: `#/${key}`, 'aria-label': label, 'aria-current': page === key || (key === 'search' && page === 'trace') ? 'page' : null }, icon(iconName), h('span', {}, label)))),
    h('div', { class: 'sidebar-foot' },
      h('div', { class: 'who', title: user.email }, user.displayName),
      h('div', { class: 'role' }, `${user.role} · ${user.email}`),
      h('button', {
        class: 'link-button',
        onclick: async () => { await api('/api/auth/logout', { method: 'POST' }); user = null; go('overview'); render(); },
      }, icon('logout'), h('span', {}, 'Sign out'))));
}

function topbar(built, page, params) {
  const actions = h('div', { class: 'top-actions' });
  if (built.rangeControl) {
    actions.append(h('div', { class: 'segmented', role: 'group', 'aria-label': 'Time range' }, RANGES.map((r) =>
      h('button', { type: 'button', 'aria-pressed': String(r === range), onclick: () => { range = r; go(page, { ...params, range: r }); } }, r))));
  }

  actions.append(h('button', { class: 'icon-button', type: 'button', title: 'Refresh', 'aria-label': 'Refresh', onclick: render }, icon('refresh')));
  return h('header', { class: 'topbar' },
    h('div', {}, h('h1', {}, built.title), built.subtitle ? h('div', { class: 'sub' }, built.subtitle) : null),
    actions);
}

window.addEventListener('hashchange', render);
window.addEventListener('unhandledrejection', (event) => {
  if (event.reason instanceof AuthError) {
    event.preventDefault();
    user = null;
    render();
  }
});

render();
