import { followUpStatusLabels, followUpTypeLabels, formatDateTime, type FollowUpStatus, type FollowUpType } from '../api/crm';
import { appointmentStatusLabels, referralStatusLabels, referralTypeLabels, visitStatusLabels, type ReferralType } from '../api/task3';
import { useLoad } from '../lib/useLoad';
import { ErrorBanner } from './Form';

interface TimelineEntry {
  kind: string;
  type: string | null;
  occurredAt: string | null;
  dueDate: string | null;
  referenceId: string | null;
  status: string | null;
}

const kindLabels: Record<string, string> = {
  FollowUp: 'متابعة',
  Appointment: 'موعد',
  Visit: 'زيارة',
  Treatment: 'علاج',
  Prescription: 'وصفة طبية',
  Referral: 'تحويل',
};

const statusLabels: Record<string, string> = {
  ...appointmentStatusLabels,
  ...visitStatusLabels,
  ...referralStatusLabels,
  Planned: 'مخططة',
  Active: 'نشطة',
};

/**
 * Audience timeline (FR-PAT-010): the route decides the content — the Doctor sees clinical entries (visits, treatments,
 * prescriptions, referrals), Sales operational ones (appointments, follow-ups, referrals), the Manager both. Payments
 * are added by Finance in Task 4.
 */
export function PatientTimeline({ url }: { url: string }) {
  const { data, error } = useLoad<TimelineEntry[]>(url);

  const typeLabel = (e: TimelineEntry) =>
    e.kind === 'FollowUp' && e.type ? followUpTypeLabels[e.type as FollowUpType] ?? e.type
      : e.kind === 'Referral' && e.type ? referralTypeLabels[e.type as ReferralType] ?? e.type : e.type ?? '—';
  const statusLabel = (e: TimelineEntry) =>
    e.kind === 'FollowUp' && e.status ? followUpStatusLabels[e.status as FollowUpStatus] ?? e.status
      : e.status ? statusLabels[e.status] ?? e.status : '—';

  return (
    <div className="card">
      <h2>السجل الزمني للمريض</h2>
      <ErrorBanner error={error} />
      {data && data.length === 0 && <p className="hint">لا توجد عناصر بعد.</p>}
      {data && data.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>العنصر</th>
              <th>النوع</th>
              <th>التاريخ</th>
              <th>تاريخ الاستحقاق</th>
              <th>الحالة</th>
            </tr>
          </thead>
          <tbody>
            {data.map((e, i) => (
              <tr key={e.referenceId ?? `${e.kind}-${i}`}>
                <td>{kindLabels[e.kind] ?? e.kind}</td>
                <td>{typeLabel(e)}</td>
                <td>{e.occurredAt ? formatDateTime(e.occurredAt) : '—'}</td>
                <td>{e.dueDate ?? '—'}</td>
                <td>{statusLabel(e)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
