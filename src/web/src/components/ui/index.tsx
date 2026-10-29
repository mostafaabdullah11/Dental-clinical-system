import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { ApiError } from '../../api/client';
import { messageFor } from '../../api/messages';
import { appointmentStatusLabels, type AppointmentStatus } from '../../api/task3';
import { Icon, type IconName } from './Icon';

export { Icon, type IconName };

export interface Crumb {
  label: string;
  to?: string;
}

/** Page title, optional breadcrumbs, short description and the page's primary actions. */
export function PageHeader({ title, lead, crumbs, actions }: { title: string; lead?: string; crumbs?: Crumb[]; actions?: ReactNode }) {
  return (
    <div className="page-header">
      <div>
        {crumbs && crumbs.length > 0 && (
          <nav className="crumbs" aria-label="مسار الصفحة">
            {crumbs.map((c, i) => (
              <span key={c.label}>
                {c.to ? <Link to={c.to}>{c.label}</Link> : c.label}
                {i < crumbs.length - 1 ? ' ›' : ''}
              </span>
            ))}
          </nav>
        )}
        <h1>{title}</h1>
        {lead && <p className="lead">{lead}</p>}
      </div>
      {actions && <div className="page-actions">{actions}</div>}
    </div>
  );
}

export type Tone = 'info' | 'success' | 'warning' | 'danger' | 'neutral';

/** A KPI tile; becomes a link when `to` is given (quick navigation). */
export function StatCard({ label, value, unit, hint, icon, tone, to }: {
  label: string;
  value: ReactNode;
  unit?: string;
  hint?: ReactNode;
  icon?: IconName;
  tone?: Exclude<Tone, 'info' | 'neutral'>;
  to?: string;
}) {
  const body = (
    <>
      {icon && (
        <span className="stat-icon">
          <Icon name={icon} />
        </span>
      )}
      <span>
        <span className="stat-label">{label}</span>
        <span className="stat-value">
          {value}
          {unit && <small>{unit}</small>}
        </span>
        {hint && <span className="stat-hint">{hint}</span>}
      </span>
    </>
  );
  const className = `stat${tone ? ` tone-${tone}` : ''}`;
  if (to?.startsWith('#')) {
    return (
      <a href={to} className={className}>
        {body}
      </a>
    );
  }
  return to ? (
    <Link to={to} className={className}>
      {body}
    </Link>
  ) : (
    <div className={className}>{body}</div>
  );
}

/** A titled panel. Tables placed directly inside keep the panel's rounded corners. */
export function Section({ title, sub, actions, children, pad }: { title: string; sub?: string; actions?: ReactNode; children: ReactNode; pad?: boolean }) {
  return (
    <section className="section">
      <header>
        <div>
          <h2>{title}</h2>
          {sub && <span className="sub">{sub}</span>}
        </div>
        {actions}
      </header>
      {pad ? <div className="body-pad">{children}</div> : children}
    </section>
  );
}

export function EmptyState({ title, hint, icon = 'inbox' }: { title: string; hint?: string; icon?: IconName }) {
  return (
    <div className="empty">
      <span className="empty-icon">
        <Icon name={icon} />
      </span>
      <strong>{title}</strong>
      {hint && <span>{hint}</span>}
    </div>
  );
}

export function Loading({ label = 'جارٍ التحميل…' }: { label?: string }) {
  return (
    <div className="loading" role="status">
      <span className="spinner" aria-hidden="true" />
      {label}
    </div>
  );
}

/** Error text for a failed load (the code is mapped to the Arabic message; never the raw server text). */
export function LoadError({ error }: { error: unknown }) {
  const code = error instanceof ApiError ? error.code : 'internal_error';
  return (
    <div className="error" role="alert">
      {messageFor(code)}
    </div>
  );
}

export function Badge({ tone = 'neutral', children }: { tone?: Tone; children: ReactNode }) {
  return <span className={`badge tone-${tone}`}>{children}</span>;
}

/** The seven frozen appointment statuses, one tone each. Arrival is never a status. */
const appointmentTones: Record<string, Tone> = {
  Scheduled: 'neutral',
  Confirmed: 'info',
  InTreatment: 'warning',
  Completed: 'success',
  Cancelled: 'danger',
  NoShow: 'danger',
  Rescheduled: 'neutral',
};

export function appointmentTone(status: string): Tone {
  return appointmentTones[status] ?? 'neutral';
}

/** A plain list that shows the first rows and expands on request (long dashboard lists stay reachable without a very tall page). */
export function ShowMoreList<T>({ items, limit = 8, render, keyOf }: { items: T[]; limit?: number; render: (item: T) => ReactNode; keyOf: (item: T) => string }) {
  const [all, setAll] = useState(false);
  const shown = all ? items : items.slice(0, limit);
  return (
    <>
      <ul className="list-plain">
        {shown.map((item) => (
          <li key={keyOf(item)}>{render(item)}</li>
        ))}
      </ul>
      {items.length > limit && (
        <div className="more">
          <button type="button" className="link" onClick={() => setAll((v) => !v)}>
            {all ? 'عرض أقل' : `عرض الكل (${items.length})`}
          </button>
        </div>
      )}
    </>
  );
}

/** Appointment status as a coloured badge (the seven frozen statuses; arrival is shown separately). */
export function AppointmentStatusBadge({ status }: { status: AppointmentStatus }) {
  return <Badge tone={appointmentTone(status)}>{appointmentStatusLabels[status]}</Badge>;
}

/** An in-page confirmation (native confirm dialogs are not allowed). */
export function ConfirmDialog({ title, message, confirmLabel = 'تأكيد', cancelLabel = 'إلغاء', danger, onConfirm, onCancel }: {
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  danger?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const confirmRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    confirmRef.current?.focus();
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onCancel();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onCancel]);

  return (
    <div className="dialog-backdrop" role="presentation" onMouseDown={(e) => e.target === e.currentTarget && onCancel()}>
      <div className="dialog" role="alertdialog" aria-modal="true" aria-labelledby="confirm-title" aria-describedby="confirm-message">
        <h2 id="confirm-title">{title}</h2>
        <p id="confirm-message">{message}</p>
        <div className="actions">
          <button type="button" ref={confirmRef} className={danger ? 'danger-button' : undefined} onClick={onConfirm}>
            {confirmLabel}
          </button>
          <button type="button" className="secondary" onClick={onCancel}>
            {cancelLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
