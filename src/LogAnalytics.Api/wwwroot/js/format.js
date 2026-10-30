const number = new Intl.NumberFormat(undefined);
const compact = new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 });
const time = new Intl.DateTimeFormat(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false });
const dayTime = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false });
const relative = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });

export const fmt = {
  number: (n) => number.format(n),
  compact: (n) => (n >= 10000 ? compact.format(n) : number.format(n)),
  percent: (r) => `${(r * 100).toFixed(r > 0 && r < 0.001 ? 2 : 1)}%`,
  time: (iso) => {
    const d = new Date(iso);
    return `${time.format(d)}.${String(d.getMilliseconds()).padStart(3, '0')}`;
  },
  dayTime: (iso) => dayTime.format(new Date(iso)),
  ago: (iso) => {
    const seconds = (new Date(iso).getTime() - Date.now()) / 1000;
    const steps = [[60, 'second'], [3600, 'minute'], [86400, 'hour'], [Infinity, 'day']];
    const divisors = { second: 1, minute: 60, hour: 3600, day: 86400 };
    const [, unit] = steps.find(([limit]) => Math.abs(seconds) < limit);
    return relative.format(Math.round(seconds / divisors[unit]), unit);
  },
  rangeLabel: (range) => ({ '15m': 'last 15 minutes', '1h': 'last hour', '24h': 'last 24 hours', '7d': 'last 7 days' })[range] ?? range,
};
