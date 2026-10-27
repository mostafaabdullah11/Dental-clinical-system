import { useState } from 'react';
import { api } from '../api/client';
import type { OvertimeEntry } from '../api/task4';
import type { ManagerAssistant } from '../api/types';
import { ErrorBanner } from '../components/Form';
import { isDecimalText, toTwoDecimals } from '../lib/decimal';
import { useLoad } from '../lib/useLoad';

/** FR-STAFF-015..017: approved Assistant overtime; Amount = Approved Hours × Hourly Rate Snapshot. Void (never delete). */
export function OvertimeCard({ assistants, today }: { assistants: ManagerAssistant[]; today: string }) {
  const [assistantId, setAssistantId] = useState('');
  return (
    <div className="card">
      <h2>الوقت الإضافي للمساعدين</h2>
      <select value={assistantId} onChange={(e) => setAssistantId(e.target.value)}>
        <option value="">— اختر المساعد —</option>
        {assistants.map((a) => (
          <option key={a.id} value={a.id}>
            {a.fullName} {a.overtimeHourlyRate ? `(${a.overtimeHourlyRate} ج.م/ساعة)` : '(سعر الساعة غير مضبوط)'}
          </option>
        ))}
      </select>
      {assistantId && <Entries key={assistantId} assistantId={assistantId} today={today} />}
    </div>
  );
}

function Entries({ assistantId, today }: { assistantId: string; today: string }) {
  const url = `/api/manager/staff/assistants/${assistantId}/overtime`;
  const { data, error, reload } = useLoad<OvertimeEntry[]>(url);
  const [workDate, setWorkDate] = useState(today);
  const [hours, setHours] = useState('');
  const [actionError, setActionError] = useState<unknown>(null);

  async function run(action: () => Promise<unknown>) {
    setActionError(null);
    try {
      await action();
      await reload();
    } catch (e) {
      setActionError(e);
    }
  }

  return (
    <>
      <ErrorBanner error={error ?? actionError} />
      <div className="inline">
        <input type="date" value={workDate} onChange={(e) => setWorkDate(e.target.value)} />
        <input placeholder="الساعات المعتمدة" value={hours} onChange={(e) => setHours(e.target.value)} inputMode="decimal" />
        <button
          type="button"
          disabled={!hours}
          onClick={() => void run(() => api.post(url, { workDate, approvedHours: isDecimalText(hours) ? toTwoDecimals(hours) : hours, attendanceRecordId: null, notes: null }))}
        >
          تسجيل
        </button>
      </div>
      <table>
        <tbody>
          {data?.map((o) => (
            <tr key={o.id}>
              <td>{o.workDate}</td>
              <td>{o.approvedHours} ساعة × {o.hourlyRateSnapshot}</td>
              <td>{o.amount} ج.م</td>
              <td>
                {o.voidedAt ? (
                  `ملغي: ${o.voidReason ?? ''}`
                ) : (
                  <button type="button" onClick={() => void run(() => api.post(`/api/manager/staff/assistants/overtime/${o.id}/void`, { reason: 'إلغاء من المدير' }))}>
                    إلغاء
                  </button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
