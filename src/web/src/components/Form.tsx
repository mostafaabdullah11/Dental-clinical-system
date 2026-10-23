import type { ReactNode } from 'react';
import { ApiError } from '../api/client';
import { fieldMessageFor, messageFor } from '../api/messages';

export function ErrorBanner({ error }: { error: unknown }) {
  if (!error) {
    return null;
  }
  const code = error instanceof ApiError ? error.code : 'internal_error';
  return (
    <div className="error" role="alert">
      {messageFor(code)}
    </div>
  );
}

export function Field({ label, field, error, children }: { label: string; field: string; error?: unknown; children: ReactNode }) {
  const codes = error instanceof ApiError ? error.fieldErrors[field] ?? [] : [];
  return (
    <label className="field">
      <span>{label}</span>
      {children}
      {codes.map((code) => (
        <small key={code} className="field-error">
          {fieldMessageFor(code)}
        </small>
      ))}
    </label>
  );
}

export function StatusBadge({ active }: { active: boolean }) {
  return <span className={active ? 'badge on' : 'badge off'}>{active ? 'نشط' : 'غير نشط'}</span>;
}
