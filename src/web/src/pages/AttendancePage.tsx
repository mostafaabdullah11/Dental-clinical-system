import { useState } from 'react';
import { api } from '../api/client';
import { attendanceStatusLabels, type AttendanceRecord, type AttendanceStatus } from '../api/task4';
import type { ManagerAssistant } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner } from '../components/Form';
import { SalesEmployeePicker } from '../components/SalesEmployeePicker';
import { clinicToday } from '../lib/time';
import { useLoad } from '../lib/useLoad';
import { OvertimeCard } from './OvertimeCard';

/** FR-STAFF-012..014 (Manager only): Attendance of Assistants and Sales / Reception; no self-check-in, no automatic deduction. */
export function AttendancePage() {
  const { me } = useAuth();
  const today = clinicToday(me?.clinicTimezone);
  const [from, setFrom] = useState(today.slice(0, 8) + '01');
  const [to, setTo] = useState(today);
  const records = useLoad<AttendanceRecord[]>(`/api/manager/staff/attendance?from=${from}&to=${to}`);
  const assistants = useLoad<ManagerAssistant[]>('/api/manager/staff/assistants');
  const [kind, setKind] = useState<'assistant' | 'sales'>('assistant');
  const [person, setPerson] = useState('');
  const [date, setDate] = useState(today);
  const [status, setStatus] = useState<AttendanceStatus>('Present');
  const [checkIn, setCheckIn] = useState('');
  const [checkOut, setCheckOut] = useState('');
  const [error, setError] = useState<unknown>(null);

  async function run(action: () => Promise<unknown>) {
    setError(null);
    try {
      await action();
      await records.reload();
    } catch (e) {
      setError(e);
    }
  }

  const create = () =>
    api.post('/api/manager/staff/attendance', {
      assistantId: kind === 'assistant' ? person || null : null,
      salesEmployeeId: kind === 'sales' ? person || null : null,
      date,
      status,
      checkInTime: status === 'Absent' || !checkIn ? null : `${checkIn}:00`,
      checkOutTime: status === 'Absent' || !checkOut ? null : `${checkOut}:00`,
      notes: null,
    });

  return (
    <section>
      <h1>الحضور والوقت الإضافي</h1>
      <p className="hint">يسجل المدير الحضور يدوياً. الحضور وحده لا يخصم من الراتب.</p>
      <ErrorBanner error={error ?? records.error} />
      <div className="card inline">
        <h2>تسجيل حضور</h2>
        <select value={kind} onChange={(e) => { setKind(e.target.value as 'assistant' | 'sales'); setPerson(''); }}>
          <option value="assistant">مساعد</option>
          <option value="sales">مبيعات / استقبال</option>
        </select>
        {kind === 'assistant' ? (
          <select value={person} onChange={(e) => setPerson(e.target.value)}>
            <option value="">— اختر —</option>
            {assistants.data?.filter((a) => a.isActive).map((a) => (
              <option key={a.id} value={a.id}>{a.fullName}</option>
            ))}
          </select>
        ) : (
          <SalesEmployeePicker value={person} onChange={setPerson} />
        )}
        <input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        <select value={status} onChange={(e) => setStatus(e.target.value as AttendanceStatus)}>
          {Object.entries(attendanceStatusLabels).map(([k, label]) => (
            <option key={k} value={k}>{label}</option>
          ))}
        </select>
        <input type="time" value={checkIn} onChange={(e) => setCheckIn(e.target.value)} disabled={status === 'Absent'} />
        <input type="time" value={checkOut} onChange={(e) => setCheckOut(e.target.value)} disabled={status === 'Absent'} />
        <button type="button" disabled={!person} onClick={() => void run(create)}>تسجيل</button>
      </div>
      <div className="inline">
        <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
      </div>
      <table>
        <thead>
          <tr>
            <th>التاريخ</th>
            <th>الموظف</th>
            <th>الحالة</th>
            <th>الحضور</th>
            <th>الانصراف</th>
            <th>تعديل الحالة</th>
          </tr>
        </thead>
        <tbody>
          {records.data?.map((r) => (
            <tr key={r.id}>
              <td>{r.date}</td>
              <td>{r.employeeName} ({r.employeeKind === 'Assistant' ? 'مساعد' : 'مبيعات'})</td>
              <td>{attendanceStatusLabels[r.status]}</td>
              <td>{r.checkInTime?.slice(0, 5) ?? '—'}</td>
              <td>{r.checkOutTime?.slice(0, 5) ?? '—'}</td>
              <td>
                <select
                  value={r.status}
                  onChange={(e) => {
                    const next = e.target.value as AttendanceStatus;
                    void run(() =>
                      api.put(`/api/manager/staff/attendance/${r.id}`, {
                        status: next,
                        checkInTime: next === 'Absent' ? null : r.checkInTime,
                        checkOutTime: next === 'Absent' ? null : r.checkOutTime,
                        notes: r.notes,
                      }, r.version),
                    );
                  }}
                >
                  {Object.entries(attendanceStatusLabels).map(([k, label]) => (
                    <option key={k} value={k}>{label}</option>
                  ))}
                </select>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <OvertimeCard assistants={assistants.data ?? []} today={today} />
    </section>
  );
}
