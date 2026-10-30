export class AuthError extends Error {}

export class ApiError extends Error {
  constructor(message, status, code) {
    super(message);
    this.status = status;
    this.code = code;
  }
}

/** Same-origin requests; the session cookie goes along by itself. */
export async function api(path, { method = 'GET', body } = {}) {
  const response = await fetch(path, {
    method,
    headers: body === undefined ? { Accept: 'application/json' } : { Accept: 'application/json', 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (response.status === 401 && path !== '/api/auth/login') throw new AuthError('Signed out');
  if (response.status === 204) return null;

  const text = await response.text();
  const data = text ? JSON.parse(text) : null;
  if (!response.ok) {
    const message = data?.detail ?? data?.title ?? `The request failed (${response.status}).`;
    const fields = data?.errors ? Object.values(data.errors).flat().join(' ') : '';
    throw new ApiError(fields || message, response.status, data?.code);
  }

  return data;
}

export function query(params) {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    for (const item of [].concat(value ?? [])) {
      if (item !== '' && item !== null && item !== undefined) search.append(key, item);
    }
  }

  const text = search.toString();
  return text ? `?${text}` : '';
}
