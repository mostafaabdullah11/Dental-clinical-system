// Minimal API client. The session is an HttpOnly cookie; nothing is ever kept in browser storage.
// Every unsafe request carries the anti-forgery token in X-CSRF-TOKEN (held in memory only).

export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly fieldErrors: Record<string, string[]>;

  constructor(status: number, code: string, fieldErrors: Record<string, string[]> = {}) {
    super(code);
    this.status = status;
    this.code = code;
    this.fieldErrors = fieldErrors;
  }
}

let csrfToken: string | null = null;
let onUnauthenticated: (() => void) | null = null;

export function setUnauthenticatedHandler(handler: () => void): void {
  onUnauthenticated = handler;
}

export async function refreshCsrf(): Promise<void> {
  const response = await fetch('/api/auth/csrf', { credentials: 'same-origin', cache: 'no-store' });
  if (!response.ok) {
    throw await toError(response);
  }
  csrfToken = ((await response.json()) as { token: string }).token;
}

export function clearCsrf(): void {
  csrfToken = null;
}

export interface Versioned<T> {
  data: T;
  etag: string | null;
}

type Method = 'GET' | 'POST' | 'PUT' | 'PATCH';

async function send(method: Method, url: string, body?: unknown, ifMatch?: string | null, idempotencyKey?: string): Promise<Response> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (idempotencyKey) {
    headers['Idempotency-Key'] = idempotencyKey;
  }
  if (method !== 'GET') {
    if (!csrfToken) {
      await refreshCsrf();
    }
    headers['X-CSRF-TOKEN'] = csrfToken ?? '';
  }
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }
  if (ifMatch) {
    headers['If-Match'] = ifMatch;
  }

  const response = await fetch(url, {
    method,
    headers,
    credentials: 'same-origin',
    cache: 'no-store',
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (response.status === 401 && url !== '/api/auth/login') {
    onUnauthenticated?.();
  }
  if (!response.ok) {
    throw await toError(response);
  }
  return response;
}

async function toError(response: Response): Promise<ApiError> {
  try {
    const problem = (await response.json()) as { code?: string; errors?: Record<string, string[]> };
    return new ApiError(response.status, problem.code ?? 'internal_error', problem.errors ?? {});
  } catch {
    return new ApiError(response.status, 'internal_error');
  }
}

async function json<T>(response: Response): Promise<T> {
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

export const api = {
  get: async <T>(url: string): Promise<T> => json<T>(await send('GET', url)),
  getVersioned: async <T>(url: string): Promise<Versioned<T>> => {
    const response = await send('GET', url);
    return { data: await json<T>(response), etag: response.headers.get('ETag') };
  },
  post: async <T>(url: string, body?: unknown, ifMatch?: string | null): Promise<T> =>
    json<T>(await send('POST', url, body, ifMatch)),
  put: async <T>(url: string, body: unknown, ifMatch?: string | null): Promise<T> =>
    json<T>(await send('PUT', url, body, ifMatch)),
  patch: async <T>(url: string, body: unknown, ifMatch?: string | null): Promise<T> =>
    json<T>(await send('PATCH', url, body, ifMatch)),
  /** Money commands (API A7): the caller keeps one key per user action so a retry never records twice. */
  postKeyed: async <T>(url: string, body: unknown, idempotencyKey: string): Promise<T> =>
    json<T>(await send('POST', url, body, null, idempotencyKey)),
  /** Report exports (FR-REPORT-008): streamed CSV saved by the browser; nothing is kept in storage. */
  download: async (url: string, fileName: string): Promise<void> => {
    const response = await send('GET', url);
    const blob = await response.blob();
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(link.href);
  },
  putVersioned: async <T>(url: string, body: unknown, ifMatch: string | null): Promise<Versioned<T>> => {
    const response = await send('PUT', url, body, ifMatch);
    return { data: await json<T>(response), etag: response.headers.get('ETag') };
  },
};

/** A fresh Idempotency-Key for one user action. */
export function newIdempotencyKey(): string {
  return crypto.randomUUID();
}
