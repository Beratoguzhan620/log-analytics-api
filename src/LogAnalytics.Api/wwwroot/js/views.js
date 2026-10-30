import { api, query, ApiError } from './api.js';
import { h, icon, clear, append, skeleton, emptyState, errorState, levelBadge } from './dom.js';
import { fmt } from './format.js';
import { volumeChart, legend } from './chart.js';

/** Runs a loader into a container with loading, empty and error states. */
async function load(container, loader, render, { rows = 5, empty } = {}) {
  clear(container, skeleton(rows));
  try {
    const data = await loader();
    const isEmpty = Array.isArray(data) ? data.length === 0 : empty?.test?.(data);
    clear(container, isEmpty ? empty.view() : render(data));
  } catch (error) {
    if (!(error instanceof ApiError)) throw error;
    clear(container, errorState(error.message, () => load(container, loader, render, { rows, empty })));
  }
}

const card = (title, hint, ...body) =>
  h('section', { class: 'card' }, h('div', { class: 'card-head' }, h('h2', {}, title), hint ? h('span', { class: 'hint' }, hint) : null), ...body);

// Overview ----------------------------------------------------------------------------------------

export function overview(ctx) {
  const kpis = h('div', { class: 'kpis' });
  const chart = h('div', { class: 'card-body' });
  const errors = h('div');
  const services = h('div');
  const range = ctx.range;

  const view = h('div', {},
    kpis,
    card('Events over time', null, h('div', { class: 'card-body', style: 'padding-bottom:0' }, legend()), chart),
    h('div', { class: 'grid-2' },
      card('Most frequent errors', 'grouped by message template', errors),
      card('Services', null, services)));

  load(kpis, () => api(`/api/stats/overview${query({ range })}`), (o) => [
    kpi('activity', 'Events', fmt.compact(o.total), fmt.rangeLabel(range)),
    kpi('warning', 'Errors', fmt.compact(o.errors), `${fmt.number(o.warnings)} warnings`, o.errors > 0),
    kpi('activity', 'Error rate', fmt.percent(o.errorRate), 'errors of all events', o.errorRate >= 0.05),
    kpi('server', 'Services', fmt.number(o.services.length), 'sending in this window'),
  ], { rows: 1 });

  load(chart, () => api(`/api/stats/volume${query({ range })}`), (v) => volumeChart(v), {
    rows: 6,
    empty: { test: (v) => v.points.every((p) => p.total === 0), view: () => emptyState('No events in this window', 'Send some logs or pick a longer range.') },
  });

  load(errors, () => api(`/api/stats/top-errors${query({ range, limit: 8 })}`), (groups) =>
    h('ul', { class: 'list' }, groups.map((g) => h('li', {
      class: 'error-item',
      tabindex: 0,
      onclick: () => ctx.go('search', { service: g.service, level: 'Error', q: searchWords(g.template), range }),
      onkeydown: (e) => { if (e.key === 'Enter') e.currentTarget.click(); },
    },
    h('span', { class: 'count' }, fmt.compact(g.count)),
    h('div', {},
      h('div', { class: 'template' }, g.template),
      h('div', { class: 'meta' }, h('span', { class: 'chip' }, g.service), h('span', {}, `last ${fmt.ago(g.lastSeen)}`),
        g.sampleException ? h('span', { class: 'mono' }, truncate(g.sampleException, 60)) : null))))),
  { rows: 6, empty: { view: () => emptyState('No errors', 'Nothing at Error level or worse in this window.', 'ok') } });

  load(services, () => api(`/api/stats/overview${query({ range })}`).then((o) => o.services), (rows) =>
    h('table', {},
      h('thead', {}, h('tr', {}, h('th', {}, 'Service'), h('th', { class: 'num' }, 'Events'), h('th', { class: 'num' }, 'Errors'), h('th', {}, 'Error rate'))),
      h('tbody', {}, rows.map((s) => h('tr', { class: 'clickable', onclick: () => ctx.go('search', { service: s.service, range }) },
        h('td', {}, s.service),
        h('td', { class: 'num' }, fmt.compact(s.total)),
        h('td', { class: 'num' }, fmt.compact(s.errors)),
        h('td', {}, h('span', { class: 'rate' }, h('span', { class: 'rate-bar' }, h('span', { style: `width:${Math.min(100, s.errorRate * 400)}%` })), fmt.percent(s.errorRate))))))),
  { rows: 5, empty: { view: () => emptyState('No services yet', 'Services appear when their logs arrive.', 'server') } });

  return { title: 'Overview', subtitle: `Everything sent in the ${fmt.rangeLabel(range)}`, view, rangeControl: true };
}

function kpi(iconName, label, value, note, bad = false) {
  return h('div', { class: 'card kpi' }, h('div', { class: 'label' }, icon(iconName), label), h('div', { class: `value${bad ? ' bad' : ''}` }, value), h('div', { class: 'note' }, note));
}

/** The literal words of a template, so a click on an error group finds its events. */
function searchWords(template) {
  const words = template.replace(/\{[^}]*\}/g, ' ').match(/[\p{L}\p{N}]{3,}/gu) ?? [];
  return words.slice(0, 4).join(' ');
}

const truncate = (text, length) => (text.length > length ? `${text.slice(0, length - 1)}…` : text);

// Search ------------------------------------------------------------------------------------------

export function search(ctx) {
  const params = ctx.params;
  const results = h('div', { class: 'events' });
  const textInput = h('input', { class: 'input', id: 'q', type: 'search', value: params.q ?? '', placeholder: 'Words, "a phrase", or, -exclude', autocomplete: 'off' });
  const levelSelect = h('select', { class: 'input', id: 'level' },
    [['', 'Any level'], ['Debug', 'Debug and up'], ['Information', 'Information and up'], ['Warning', 'Warning and up'], ['Error', 'Error and up']]
      .map(([value, label]) => h('option', { value, selected: (params.level ?? '') === value }, label)));
  const serviceSelect = h('select', { class: 'input', id: 'service' }, h('option', { value: '' }, 'All services'));
  const propInput = h('input', { class: 'input', id: 'prop', value: params.prop ?? '', placeholder: 'OrderId:SIP-1001', autocomplete: 'off' });

  api(`/api/stats/overview${query({ range: '7d' })}`).then((o) => {
    for (const s of o.services) serviceSelect.append(h('option', { value: s.service, selected: params.service === s.service }, s.service));
    if (params.service && !o.services.some((s) => s.service === params.service)) serviceSelect.append(h('option', { value: params.service, selected: true }, params.service));
  }).catch(() => {});

  const submit = (event) => {
    event?.preventDefault();
    ctx.go('search', { q: textInput.value.trim(), level: levelSelect.value, service: serviceSelect.value, prop: propInput.value.trim(), range: ctx.range });
  };

  const form = h('form', { class: 'filters', onsubmit: submit, role: 'search' },
    h('div', { class: 'field' }, h('label', { for: 'q' }, 'Text'), h('div', { class: 'input-icon' }, icon('search'), textInput)),
    h('div', { class: 'field' }, h('label', { for: 'level' }, 'Level'), levelSelect),
    h('div', { class: 'field' }, h('label', { for: 'service' }, 'Service'), serviceSelect),
    h('div', { class: 'field' }, h('label', { for: 'prop' }, 'Property'), propInput),
    h('button', { class: 'button', type: 'submit' }, icon('search'), 'Search'));

  const filters = { q: params.q, level: params.level, service: params.service, prop: params.prop, range: ctx.range, traceId: params.traceId };
  const loadPage = async (cursor, append) => {
    const more = results.querySelector('.more');
    if (!append) clear(results, skeleton(8));
    else if (more) more.querySelector('button').disabled = true;
    try {
      const page = await api(`/api/events${query({ ...filters, cursor, limit: 50 })}`);
      if (!append) results.replaceChildren();
      more?.remove();
      if (!append && page.items.length === 0) {
        results.append(emptyState('No events match', 'Try fewer filters or a longer time range.', 'search'));
        return;
      }

      for (const event of page.items) results.append(...eventRow(event, ctx));
      if (page.nextCursor) {
        results.append(h('div', { class: 'more' }, h('button', { class: 'button secondary', onclick: () => loadPage(page.nextCursor, true) }, 'Load older events')));
      }
    } catch (error) {
      if (!(error instanceof ApiError)) throw error;
      if (!append) clear(results, errorState(error.message, () => loadPage(cursor, append)));
    }
  };

  loadPage(null, false);
  return {
    title: 'Search',
    subtitle: filters.traceId ? `Trace ${filters.traceId}` : `Newest first, ${fmt.rangeLabel(ctx.range)}`,
    view: h('section', { class: 'card' }, form, results),
    rangeControl: true,
  };
}

function eventRow(event, ctx) {
  const detail = h('div', { class: 'detail', hidden: true });
  const row = h('div', {
    class: 'event-row', role: 'button', tabindex: 0, 'aria-expanded': 'false',
    onclick: () => toggle(), onkeydown: (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); toggle(); } },
  },
  h('span', { class: 'time', title: new Date(event.timestamp).toISOString() }, fmt.time(event.timestamp)),
  levelBadge(event.level),
  h('span', { class: 'svc' }, event.service),
  h('span', { class: 'msg' }, event.message),
  h('span', { class: 'chev' }, icon('chevron')));

  function toggle() {
    const open = row.getAttribute('aria-expanded') !== 'true';
    row.setAttribute('aria-expanded', String(open));
    detail.hidden = !open;
    if (open && detail.childElementCount === 0) fillDetail(detail, event, ctx);
  }

  return [row, detail];
}

function fillDetail(detail, event, ctx) {
  const properties = Object.entries(event.properties ?? {});
  append(detail, [
    h('h3', {}, 'Message'),
    h('p', { class: 'full' }, event.message),
    event.messageTemplate ? [h('h3', {}, 'Template'), h('p', { class: 'full mono' }, event.messageTemplate)] : null,
    event.exception ? [h('h3', {}, 'Exception'), h('pre', { class: 'exception' }, event.exception)] : null,
    h('h3', {}, 'Properties'),
    properties.length === 0
      ? h('p', { class: 'muted' }, 'None.')
      : h('dl', { class: 'props' }, properties.flatMap(([key, value]) => [h('dt', {}, key), h('dd', {}, typeof value === 'string' ? value : JSON.stringify(value))])),
    h('h3', {}, 'Where'),
    h('dl', { class: 'props' },
      h('dt', {}, 'Time'), h('dd', {}, new Date(event.timestamp).toISOString()),
      h('dt', {}, 'Service'), h('dd', {}, event.service),
      h('dt', {}, 'Source'), h('dd', {}, event.source ?? '—'),
      event.traceId ? [h('dt', {}, 'Trace'), h('dd', {}, event.traceId)] : null),
    h('div', { class: 'detail-actions' },
      event.traceId ? h('a', { class: 'button secondary', href: `#/trace/${encodeURIComponent(event.traceId)}` }, icon('trace'), 'View trace') : null,
      h('button', { class: 'button secondary', onclick: (e) => copy(e.currentTarget, event.message) }, icon('copy'), 'Copy message')),
  ]);
}

async function copy(button, text) {
  try {
    await navigator.clipboard.writeText(text);
    button.lastChild.textContent = 'Copied';
  } catch {
    button.lastChild.textContent = 'Copy not allowed';
  }
}

// Trace -------------------------------------------------------------------------------------------

export function trace(ctx) {
  const list = h('div');
  const id = ctx.traceId;
  load(list, () => api(`/api/traces/${encodeURIComponent(id)}`), (events) => {
    const start = new Date(events[0].timestamp).getTime();
    return h('ol', { class: 'timeline' }, events.map((e) => h('li', {},
      h('span', { class: 'offset' }, `+${fmt.number(new Date(e.timestamp).getTime() - start)} ms`),
      h('span', { class: `dot ${e.level}` }),
      h('span', { class: 'chip' }, e.service),
      h('span', { class: 'lvl' }, levelBadge(e.level)),
      h('span', { class: 'msg' }, e.message))));
  }, { rows: 6, empty: { view: () => emptyState('Nothing in this trace', 'Its events may be older than the retention period.', 'trace') } });

  return {
    title: 'Trace',
    subtitle: id,
    view: h('div', {},
      h('p', {}, h('a', { href: '#/search', onclick: (e) => { if (history.length > 1) { e.preventDefault(); history.back(); } } }, '← Back')),
      card('Every event of this request, across services', 'oldest first', list)),
  };
}

// Alerts ------------------------------------------------------------------------------------------

export function alerts(ctx) {
  const rules = h('div');
  const firings = h('div');
  const formArea = h('div');

  const loadRules = () => load(rules, () => api('/api/alerts/rules'), (items) => h('table', {},
    h('thead', {}, h('tr', {}, h('th', {}, 'Rule'), h('th', {}, 'Fires when'), h('th', {}, 'Sends to'), h('th', {}, 'Last fired'), h('th', {}, ''))),
    h('tbody', {}, items.map((r) => h('tr', {},
      h('td', {}, r.name),
      h('td', {}, `≥ ${fmt.number(r.threshold)} × ${r.minLevel}+ in ${r.windowMinutes} min${r.service ? ` · ${r.service}` : ''}`),
      h('td', {}, r.channel === 'None' ? h('span', { class: 'muted' }, 'Dashboard only') : r.channel === 'Webhook' ? 'Webhook' : 'Email'),
      h('td', {}, r.lastFiredAt ? fmt.ago(r.lastFiredAt) : h('span', { class: 'muted' }, 'never')),
      h('td', {}, r.enabled ? '' : h('span', { class: 'badge-off' }, 'off')))))),
  { rows: 3, empty: { view: () => emptyState('No rules yet', ctx.user.role === 'Admin' ? 'Add one above.' : 'An admin can add rules.', 'bell') } });

  const loadFirings = () => load(firings, () => api('/api/alerts/firings?limit=30'), (items) => h('table', {},
    h('thead', {}, h('tr', {}, h('th', {}, 'When'), h('th', {}, 'Rule'), h('th', { class: 'num' }, 'Events'), h('th', {}, 'Latest message'), h('th', {}, 'Delivery'))),
    h('tbody', {}, items.map((f) => h('tr', {},
      h('td', { title: f.firedAt }, fmt.dayTime(f.firedAt)),
      h('td', {}, f.ruleName),
      h('td', { class: 'num' }, fmt.number(f.count)),
      h('td', {}, truncate(f.sampleMessage ?? '', 70)),
      h('td', {}, f.delivered === null ? h('span', { class: 'muted' }, '—')
        : f.delivered ? h('span', { class: 'status-ok' }, icon('ok'), 'sent')
          : h('span', { class: 'status-bad', title: f.deliveryError ?? '' }, icon('fail'), 'failed')))))),
  { rows: 4, empty: { view: () => emptyState('Nothing has fired', 'Firings show up here when a rule’s threshold is reached.', 'ok') } });

  if (ctx.user.role === 'Admin') formArea.append(ruleForm(() => { loadRules(); }));
  loadRules();
  loadFirings();
  return { title: 'Alerts', subtitle: 'Rules are checked every 30 seconds', view: h('div', { class: 'stack' }, formArea, card('Rules', null, rules), h('div', { style: 'height:14px' }), card('Recent firings', null, firings)) };
}

function ruleForm(onCreated) {
  const message = h('div');
  const fields = {
    name: h('input', { class: 'input', id: 'r-name', required: true, placeholder: 'Payment errors' }),
    service: h('input', { class: 'input', id: 'r-service', placeholder: 'payments (empty for all)' }),
    minLevel: h('select', { class: 'input', id: 'r-level' }, ['Warning', 'Error', 'Fatal'].map((l) => h('option', { value: l, selected: l === 'Error' }, l))),
    threshold: h('input', { class: 'input', id: 'r-threshold', type: 'number', min: 1, value: 20 }),
    windowMinutes: h('input', { class: 'input', id: 'r-window', type: 'number', min: 1, max: 1440, value: 5 }),
    cooldownMinutes: h('input', { class: 'input', id: 'r-cooldown', type: 'number', min: 0, max: 1440, value: 15 }),
    channel: h('select', { class: 'input', id: 'r-channel' }, [['None', 'Dashboard only'], ['Webhook', 'Webhook'], ['NotificationService', 'Email (notification-service)']].map(([v, l]) => h('option', { value: v }, l))),
    webhookUrl: h('input', { class: 'input', id: 'r-hook', type: 'url', placeholder: 'https://…' }),
  };
  const label = (id, text) => h('label', { for: id }, text);

  const submit = async (event) => {
    event.preventDefault();
    clear(message);
    try {
      await api('/api/alerts/rules', {
        method: 'POST',
        body: {
          name: fields.name.value, service: fields.service.value || null, minLevel: fields.minLevel.value,
          threshold: Number(fields.threshold.value), windowMinutes: Number(fields.windowMinutes.value),
          cooldownMinutes: Number(fields.cooldownMinutes.value), channel: fields.channel.value, webhookUrl: fields.webhookUrl.value || null,
        },
      });
      event.target.reset();
      onCreated();
    } catch (error) {
      if (!(error instanceof ApiError)) throw error;
      clear(message, h('p', { class: 'alert', role: 'alert', style: 'margin:0 16px 16px' }, error.message));
    }
  };

  return h('section', { class: 'card', style: 'margin-bottom:14px' },
    h('div', { class: 'card-head' }, h('h2', {}, 'New rule')),
    h('form', { onsubmit: submit },
      h('div', { class: 'card-body form-grid', style: 'padding:14px 16px 16px' },
        h('div', { class: 'field wide' }, label('r-name', 'Name'), fields.name),
        h('div', { class: 'field wide' }, label('r-service', 'Service'), fields.service),
        h('div', { class: 'field' }, label('r-level', 'Level and worse'), fields.minLevel),
        h('div', { class: 'field' }, label('r-threshold', 'At least … events'), fields.threshold),
        h('div', { class: 'field' }, label('r-window', 'Within … minutes'), fields.windowMinutes),
        h('div', { class: 'field' }, label('r-cooldown', 'Then quiet for … minutes'), fields.cooldownMinutes),
        h('div', { class: 'field' }, label('r-channel', 'Send to'), fields.channel),
        h('div', { class: 'field wide' }, label('r-hook', 'Webhook URL'), fields.webhookUrl),
        h('div', { class: 'field' }, h('span', { class: 'sr-only' }, 'Save'), h('button', { class: 'button', type: 'submit' }, icon('plus'), 'Add rule'))),
      message));
}

// Sources -----------------------------------------------------------------------------------------

export function sources(ctx) {
  const list = h('div');
  const created = h('div');
  const loadList = () => load(list, () => api('/api/sources'), (items) => h('table', {},
    h('thead', {}, h('tr', {}, h('th', {}, 'Name'), h('th', {}, 'Key starts with'), h('th', {}, 'Added'), h('th', {}, 'Status'))),
    h('tbody', {}, items.map((s) => h('tr', {},
      h('td', {}, s.name),
      h('td', { class: 'mono' }, `la_${s.keyPrefix}_…`),
      h('td', {}, fmt.dayTime(s.createdAt)),
      h('td', {}, s.revokedAt ? h('span', { class: 'status-bad' }, icon('fail'), 'revoked') : h('span', { class: 'status-ok' }, icon('ok'), 'active')))))),
  { rows: 3, empty: { view: () => emptyState('No sources yet', 'Add one to get a key.', 'plug') } });

  let form = null;
  if (ctx.user.role === 'Admin') {
    const name = h('input', { class: 'input', id: 's-name', required: true, placeholder: 'Checkout API' });
    const message = h('div');
    form = h('section', { class: 'card', style: 'margin-bottom:14px' },
      h('div', { class: 'card-head' }, h('h2', {}, 'Add a source'), h('span', { class: 'hint' }, 'The key can send logs and nothing else')),
      h('form', {
        class: 'filters', style: 'grid-template-columns:minmax(0,1fr) auto',
        onsubmit: async (event) => {
          event.preventDefault();
          clear(message);
          try {
            const source = await api('/api/sources', { method: 'POST', body: { name: name.value } });
            event.target.reset();
            clear(created, h('div', { class: 'secret', role: 'status' },
              h('p', { style: 'margin:0 0 6px' }, h('strong', {}, `Key for ${source.name}. `), 'It is shown only now; store it somewhere safe.'),
              h('code', {}, source.apiKey)));
            loadList();
          } catch (error) {
            if (!(error instanceof ApiError)) throw error;
            clear(message, h('p', { class: 'alert', role: 'alert', style: 'margin:0 16px 16px' }, error.message));
          }
        },
      },
      h('div', { class: 'field' }, h('label', { for: 's-name' }, 'Name'), name),
      h('button', { class: 'button', type: 'submit' }, icon('plus'), 'Add source')),
      message, created);
  }

  loadList();
  return { title: 'Sources', subtitle: 'Applications that send logs', view: h('div', {}, form, card('All sources', null, list)) };
}

// Sign-in -----------------------------------------------------------------------------------------

export function signIn(onSignedIn) {
  const message = h('div');
  const email = h('input', { class: 'input', id: 'email', type: 'email', autocomplete: 'username', required: true });
  const password = h('input', { class: 'input', id: 'password', type: 'password', autocomplete: 'current-password', required: true });
  const button = h('button', { class: 'button', type: 'submit' }, 'Sign in');
  const form = h('form', {
    onsubmit: async (event) => {
      event.preventDefault();
      button.disabled = true;
      clear(message);
      try {
        const user = await api('/api/auth/login', { method: 'POST', body: { email: email.value, password: password.value } });
        onSignedIn(user);
      } catch (error) {
        if (!(error instanceof ApiError)) throw error;
        clear(message, h('p', { class: 'alert', role: 'alert' }, error.status === 429 ? 'Too many attempts. Wait a minute and try again.' : error.message));
        button.disabled = false;
        password.select();
      }
    },
  },
  h('div', { class: 'field' }, h('label', { for: 'email' }, 'Email'), email),
  h('div', { class: 'field' }, h('label', { for: 'password' }, 'Password'), password),
  button);

  queueMicrotask(() => email.focus());
  return h('main', { class: 'signin' }, h('div', { class: 'card' },
    h('p', { class: 'eyebrow' }, 'Log Analytics'),
    h('h1', {}, 'Sign in'),
    h('p', { class: 'lead' }, 'Search your logs, see what is failing, get told when it does.'),
    message, form));
}
