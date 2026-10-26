import { Link } from 'react-router-dom';
import { api } from '../api/client';
import { notificationTypeLabels, type InboxItem } from '../api/task3';
import type { PagedResult } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner } from '../components/Form';
import { formatClinicDateTime } from '../lib/time';
import { useLoad } from '../lib/useLoad';

const targets: Record<string, string> = {
  Appointment: '/appointments',
  Referral: '/referrals',
  FeedbackRequest: '/feedback',
};

/** FR-NOTIF-007 (Sales / Reception and Manager): the in-app inbox. Doctors have no inbox in V1. */
export function NotificationsPage() {
  const { me } = useAuth();
  const { data, error, reload } = useLoad<PagedResult<InboxItem>>('/api/me/notifications?pageSize=100');

  async function markRead(id: string) {
    await api.patch(`/api/me/notifications/${id}/read`, undefined);
    void reload();
  }

  async function markAll() {
    await api.post('/api/me/notifications/read-all');
    void reload();
  }

  return (
    <section>
      <h1>الإشعارات</h1>
      <button type="button" className="secondary" onClick={() => void markAll()}>
        تعليم الكل كمقروء
      </button>
      <ErrorBanner error={error} />
      <table>
        <thead>
          <tr>
            <th>الوقت</th>
            <th>النوع</th>
            <th>المريض</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {data?.items.map((n) => (
            <tr key={n.id} className={n.readAt ? '' : 'unread'}>
              <td>{formatClinicDateTime(n.sentAt, me?.clinicTimezone)}</td>
              <td>{notificationTypeLabels[n.notificationType] ?? n.notificationType}</td>
              <td>{n.patient ? <Link to={`/patients/${n.patient.id}`}>{n.patient.patientCode} — {n.patient.fullName}</Link> : '—'}</td>
              <td className="actions">
                {targets[n.relatedEntityType] && <Link to={targets[n.relatedEntityType]!}>فتح</Link>}
                {!n.readAt && (
                  <button type="button" className="link" onClick={() => void markRead(n.id)}>
                    تعليم كمقروء
                  </button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {data && data.items.length === 0 && <p className="hint">لا توجد إشعارات.</p>}
    </section>
  );
}
