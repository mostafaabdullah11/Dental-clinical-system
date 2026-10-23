import { followUpStatusLabels, followUpTypeLabels, formatDateTime, type FollowUpStatus, type FollowUpType } from '../api/crm';
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

const kindLabels: Record<string, string> = { FollowUp: 'متابعة' };

/**
 * Audience timeline (FR-PAT-010): the route decides the content. Follow-ups are shown now; visits, treatments,
 * prescriptions, appointments and payments are added by their modules in later phases.
 */
export function PatientTimeline({ url }: { url: string }) {
  const { data, error } = useLoad<TimelineEntry[]>(url);

  const typeLabel = (e: TimelineEntry) =>
    e.kind === 'FollowUp' && e.type ? followUpTypeLabels[e.type as FollowUpType] ?? e.type : e.type ?? '—';
  const statusLabel = (e: TimelineEntry) =>
    e.kind === 'FollowUp' && e.status ? followUpStatusLabels[e.status as FollowUpStatus] ?? e.status : e.status ?? '—';

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
