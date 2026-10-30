import { h } from './dom.js';
import { fmt } from './format.js';

const SVG = 'http://www.w3.org/2000/svg';

/** Levels as the chart shows them, bottom to top. Verbose joins Debug and Fatal joins Error. */
export const SERIES = [
  { key: 'Debug', label: 'Debug', color: 'var(--debug)', levels: ['Verbose', 'Debug'] },
  { key: 'Information', label: 'Information', color: 'var(--info)', levels: ['Information'] },
  { key: 'Warning', label: 'Warning', color: 'var(--warning)', levels: ['Warning'] },
  { key: 'Error', label: 'Error', color: 'var(--error)', levels: ['Error', 'Fatal'] },
];

function el(tag, attrs) {
  const node = document.createElementNS(SVG, tag);
  for (const [k, v] of Object.entries(attrs)) node.setAttribute(k, String(v));
  return node;
}

function niceMax(value) {
  if (value <= 4) return 4;
  const power = 10 ** Math.floor(Math.log10(value));
  const steps = [1, 2, 2.5, 5, 10];
  return steps.map((s) => s * power).find((s) => s >= value) ?? value;
}

/** A stacked bar chart of events per bucket. Hovering a bar shows its counts. */
export function volumeChart(volume, { onSelect } = {}) {
  const width = 900;
  const height = 220;
  const pad = { top: 10, right: 8, bottom: 24, left: 44 };
  const points = volume.points;
  const max = niceMax(Math.max(1, ...points.map((p) => p.total)));
  const plotW = width - pad.left - pad.right;
  const plotH = height - pad.top - pad.bottom;
  const slot = plotW / Math.max(points.length, 1);
  const barW = Math.max(1, slot * 0.72);

  const svg = el('svg', { viewBox: `0 0 ${width} ${height}`, preserveAspectRatio: 'none', role: 'img', 'aria-label': 'Events over time by level' });

  for (let i = 0; i <= 4; i++) {
    const y = pad.top + plotH - (plotH * i) / 4;
    svg.append(el('line', { x1: pad.left, x2: width - pad.right, y1: y, y2: y, class: 'grid' }));
    const label = el('text', { x: pad.left - 8, y: y + 4, 'text-anchor': 'end', class: 'axis' });
    label.textContent = fmt.compact((max * i) / 4);
    svg.append(label);
  }

  const labelEvery = Math.max(1, Math.ceil(points.length / 6));
  const multiDay = new Date(volume.to) - new Date(volume.from) > 36 * 3600 * 1000;
  const tooltip = h('div', { class: 'tooltip', hidden: true });

  points.forEach((point, index) => {
    const x = pad.left + index * slot + (slot - barW) / 2;
    let y = pad.top + plotH;
    const group = el('g', { class: 'bar', tabindex: 0 });
    for (const series of SERIES) {
      const count = series.levels.reduce((sum, level) => sum + (point.counts[level] ?? 0), 0);
      if (count === 0) continue;
      const barH = (count / max) * plotH;
      y -= barH;
      group.append(el('rect', { x, y, width: barW, height: Math.max(barH, 0.5), fill: series.color, rx: 1 }));
    }

    // A transparent column makes even empty buckets easy to hover.
    group.append(el('rect', { x: pad.left + index * slot, y: pad.top, width: slot, height: plotH, fill: 'transparent' }));
    const show = () => {
      const lines = SERIES.map((s) => [s.label, s.levels.reduce((sum, level) => sum + (point.counts[level] ?? 0), 0)]);
      tooltip.replaceChildren(
        h('div', { class: 't' }, multiDay ? fmt.dayTime(point.bucket) : fmt.dayTime(point.bucket)),
        ...lines.map(([label, count]) => h('div', {}, h('span', {}, label), h('span', {}, fmt.number(count)))));
      tooltip.style.left = `${((x + barW / 2) / width) * 100}%`;
      tooltip.style.top = `${(Math.max(y, pad.top + 20) / height) * 100}%`;
      tooltip.hidden = false;
    };
    group.addEventListener('mouseenter', show);
    group.addEventListener('focus', show);
    group.addEventListener('mouseleave', () => { tooltip.hidden = true; });
    group.addEventListener('blur', () => { tooltip.hidden = true; });
    if (onSelect) group.addEventListener('click', () => onSelect(point));
    svg.append(group);

    if (index % labelEvery === 0) {
      const d = new Date(point.bucket);
      const label = el('text', { x: pad.left + index * slot + slot / 2, y: height - 6, 'text-anchor': 'middle', class: 'axis' });
      label.textContent = multiDay
        ? d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
        : d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', hour12: false });
      svg.append(label);
    }
  });

  return h('div', { class: 'chart' }, svg, tooltip);
}

export function legend() {
  return h('div', { class: 'legend' }, SERIES.map((s) => h('span', {}, h('i', { class: 'swatch', style: `background:${s.color}` }), s.label)));
}
