import { useCallback, useEffect, useState } from 'react';
import { api } from '../api/client';
import type { AvailabilityWeek } from '../api/task3';
import type { ManagerDoctor } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner } from '../components/Form';
import { addDays, clinicToday, formatDate } from '../lib/time';
import { useLoad } from '../lib/useLoad';

interface RangeRow {
  date: string;
  start: string;
  end: string;
}

/**
 * FR-SCH-001 / -002 (Manager): weekly availability per Doctor — draft ranges, then publish. Live ranges of one Doctor
 * never overlap (availability only: appointments may overlap). Saving withdraws the previous ranges (kept as history).
 */
export function AvailabilityPage() {
  const { me } = useAuth();
  const { data: doctors, error: doctorsError } = useLoad<ManagerDoctor[]>('/api/manager/staff/doctors');
  const [doctorId, setDoctorId] = useState('');
  const [weekStart, setWeekStart] = useState(() => clinicToday(me?.clinicTimezone));
  const [week, setWeek] = useState<AvailabilityWeek | null>(null);
  const [rows, setRows] = useState<RangeRow[]>([]);
  const [error, setError] = useState<unknown>(null);
  const [saved, setSaved] = useState('');

  const url = doctorId ? `/api/manager/doctors/${doctorId}/availability/${weekStart}` : null;

  const load = useCallback(async () => {
    if (!url) {
      return;
    }
    setError(null);
    try {
      const data = await api.get<AvailabilityWeek>(url);
      setWeek(data);
      setRows(data.ranges.map((r) => ({ date: r.date, start: r.start.slice(0, 5), end: r.end.slice(0, 5) })));
    } catch (e) {
      setError(e);
    }
  }, [url]);

  useEffect(() => {
    void load();
  }, [load]);

  async function save() {
    if (!url) {
      return;
    }
    setError(null);
    setSaved('');
    try {
      const data = await api.put<AvailabilityWeek>(url, { ranges: rows }, week?.version);
      setWeek(data);
      setSaved('تم حفظ المسودة.');
    } catch (e) {
      setError(e);
    }
  }

  async function publish() {
    if (!url) {
      return;
    }
    setError(null);
    setSaved('');
    try {
      setWeek(await api.post<AvailabilityWeek>(`${url}/publish`, undefined, week?.version));
      setSaved('تم نشر الإتاحة. تظهر الآن للمبيعات / الاستقبال.');
    } catch (e) {
      setError(e);
    }
  }

  const days = Array.from({ length: 7 }, (_, i) => addDays(weekStart, i));

  return (
    <section>
      <h1>إتاحة الأطباء الأسبوعية</h1>
      <div className="card inline">
        <label className="field">
          <span>الطبيب</span>
          <select value={doctorId} onChange={(e) => setDoctorId(e.target.value)}>
            <option value="">— اختر الطبيب —</option>
            {doctors?.filter((d) => d.isActive).map((d) => (
              <option key={d.id} value={d.id}>
                {d.fullName}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span>بداية الأسبوع</span>
          <input type="date" value={weekStart} onChange={(e) => setWeekStart(e.target.value)} />
        </label>
      </div>
      <ErrorBanner error={doctorsError} />
      <ErrorBanner error={error} />
      {saved && <div className="ok">{saved}</div>}
      {doctorId && week && (
        <div className="card">
          <p>
            الحالة: <strong>{week.status === 'Published' ? 'منشورة' : week.status === 'Draft' ? 'مسودة' : 'لا توجد إتاحة بعد'}</strong>
          </p>
          <table>
            <thead>
              <tr>
                <th>اليوم</th>
                <th>من</th>
                <th>إلى</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r, i) => (
                <tr key={i}>
                  <td>
                    <select value={r.date} onChange={(e) => setRows(rows.map((x, j) => (j === i ? { ...x, date: e.target.value } : x)))}>
                      {days.map((d) => (
                        <option key={d} value={d}>
                          {formatDate(d)}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td>
                    <input type="time" value={r.start} onChange={(e) => setRows(rows.map((x, j) => (j === i ? { ...x, start: e.target.value } : x)))} />
                  </td>
                  <td>
                    <input type="time" value={r.end} onChange={(e) => setRows(rows.map((x, j) => (j === i ? { ...x, end: e.target.value } : x)))} />
                  </td>
                  <td>
                    <button type="button" className="link danger" onClick={() => setRows(rows.filter((_, j) => j !== i))}>
                      حذف
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="actions">
            <button type="button" className="secondary" onClick={() => setRows([...rows, { date: weekStart, start: '09:00', end: '13:00' }])}>
              إضافة فترة
            </button>
            <button type="button" onClick={() => void save()}>
              حفظ كمسودة
            </button>
            <button type="button" onClick={() => void publish()} disabled={!week.version}>
              نشر
            </button>
          </div>
          <p className="hint">لا يُسمح بتداخل فترات الإتاحة لنفس الطبيب. أما المواعيد فيمكن أن تتداخل.</p>
        </div>
      )}
    </section>
  );
}
