import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { appointmentKindLabels, appointmentStatusLabels, type DoctorQueueItem, type DoctorSchedule, type VisitDetail } from '../api/task3';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner } from '../components/Form';
import { formatAge } from '../lib/patients';
import { addDays, clinicLocalToIso, clinicToday, formatClinicTime, formatDate } from '../lib/time';
import { useLoad } from '../lib/useLoad';

/** FR-SCH-011 / FR-CLN-001 (Doctor): today's arrived patients (derived queue) and starting the Visit from it. */
export function DoctorQueuePage() {
  const { me } = useAuth();
  const navigate = useNavigate();
  const { data, error, reload } = useLoad<DoctorQueueItem[]>('/api/doctor/queue');
  const [startError, setStartError] = useState<unknown>(null);

  async function start(item: DoctorQueueItem) {
    setStartError(null);
    try {
      const visit = await api.post<VisitDetail>('/api/doctor/visits', { patientId: item.patient.id, appointmentId: item.appointmentId });
      navigate(`/visits/${visit.id}`);
    } catch (e) {
      setStartError(e);
      void reload();
    }
  }

  return (
    <section>
      <h1>قائمة الانتظار</h1>
      <p className="hint">المرضى الذين سجّل الاستقبال حضورهم اليوم. بدء الزيارة ينقل الموعد إلى «قيد العلاج».</p>
      <ErrorBanner error={error} />
      <ErrorBanner error={startError} />
      <button type="button" className="secondary" onClick={() => void reload()}>
        تحديث
      </button>
      <table>
        <thead>
          <tr>
            <th>وقت الحضور</th>
            <th>موعد</th>
            <th>المريض</th>
            <th>العمر</th>
            <th>السبب</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {data?.map((q) => (
            <tr key={q.appointmentId}>
              <td>{formatClinicTime(q.arrivedAt, me?.clinicTimezone)}</td>
              <td>
                {formatClinicTime(q.startAt, me?.clinicTimezone)} ({q.durationMinutes} دقيقة)
              </td>
              <td>
                <Link to={`/patients/${q.patient.id}`}>
                  {q.patient.patientCode} — {q.patient.fullName}
                </Link>
              </td>
              <td>{formatAge(q.patient.ageDisplay)}</td>
              <td>{q.reason}</td>
              <td>
                <button type="button" onClick={() => void start(q)}>
                  بدء الزيارة
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {data && data.length === 0 && <p className="hint">لا يوجد مرضى في الانتظار.</p>}
    </section>
  );
}

/** FR-SCH-009 / -014 (Doctor): the week's published availability and own appointments; reschedule only own follow-ups. */
export function DoctorSchedulePage() {
  const { me } = useAuth();
  const zone = me?.clinicTimezone;
  const [weekStart, setWeekStart] = useState(() => clinicToday(zone));
  const { data, error, reload } = useLoad<DoctorSchedule>(`/api/doctor/schedule?weekStart=${weekStart}`);
  const days = Array.from({ length: 7 }, (_, i) => addDays(weekStart, i));

  return (
    <section>
      <h1>جدولي</h1>
      <div className="card inline">
        <button type="button" className="secondary" onClick={() => setWeekStart(addDays(weekStart, -7))}>
          الأسبوع السابق
        </button>
        <input type="date" value={weekStart} onChange={(e) => setWeekStart(e.target.value)} />
        <button type="button" className="secondary" onClick={() => setWeekStart(addDays(weekStart, 7))}>
          الأسبوع التالي
        </button>
        {data && <span className="hint">{data.published ? 'الإتاحة منشورة' : 'لا توجد إتاحة منشورة لهذا الأسبوع'}</span>}
      </div>
      <ErrorBanner error={error} />
      {data && days.map((d) => {
        const ranges = data.ranges.filter((r) => r.date === d);
        const appointments = data.appointments.filter((a) => toDay(a.startAt, zone) === d);
        if (ranges.length === 0 && appointments.length === 0) {
          return null;
        }
        return (
          <div className="card" key={d}>
            <h2>{formatDate(d)}</h2>
            <p className="hint">الإتاحة: {ranges.map((r) => `${r.start.slice(0, 5)}–${r.end.slice(0, 5)}`).join('، ') || '—'}</p>
            <table>
              <tbody>
                {appointments.map((a) => (
                  <tr key={a.id}>
                    <td>{formatClinicTime(a.startAt, zone)}</td>
                    <td>{a.durationMinutes} دقيقة</td>
                    <td>
                      <Link to={`/patients/${a.patient.id}`}>
                        {a.patient.patientCode} — {a.patient.fullName}
                      </Link>
                    </td>
                    <td>{a.reason}</td>
                    <td>{appointmentKindLabels[a.kind]}</td>
                    <td>{appointmentStatusLabels[a.status]}</td>
                    <td>{a.arrivedAt ? 'حضر' : ''}</td>
                    <td>{a.canReschedule && <DoctorReschedule id={a.id} version={a.version} duration={a.durationMinutes} onDone={() => void reload()} />}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        );
      })}
    </section>
  );
}

function toDay(iso: string, zone: string | null | undefined): string {
  return new Date(iso).toLocaleDateString('en-CA', { timeZone: zone ?? 'Africa/Cairo' });
}

function DoctorReschedule({ id, version, duration, onDone }: { id: string; version: string; duration: number; onDone: () => void }) {
  const { me } = useAuth();
  const [open, setOpen] = useState(false);
  const [start, setStart] = useState('');
  const [minutes, setMinutes] = useState(String(duration));
  const [reason, setReason] = useState('');
  const [error, setError] = useState<unknown>(null);

  if (!open) {
    return (
      <button type="button" className="link" onClick={() => setOpen(true)}>
        إعادة جدولة متابعتي
      </button>
    );
  }

  async function save() {
    setError(null);
    try {
      await api.post(`/api/doctor/appointments/${id}/reschedule`,
        { startAt: clinicLocalToIso(start, me?.clinicTimezone), durationMinutes: Number(minutes), rescheduleReason: reason }, version);
      setOpen(false);
      onDone();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <div className="inline wrap">
      <input type="datetime-local" value={start} onChange={(e) => setStart(e.target.value)} />
      <input type="number" min={1} max={480} value={minutes} onChange={(e) => setMinutes(e.target.value)} />
      <input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="السبب" />
      <button type="button" onClick={() => void save()} disabled={!start || !reason.trim()}>
        حفظ
      </button>
      <ErrorBanner error={error} />
    </div>
  );
}
