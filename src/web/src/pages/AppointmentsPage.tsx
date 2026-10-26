import { useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';
import {
  appointmentKindLabels, appointmentStatusLabels, isOpenAppointment,
  type AppointmentKind, type AvailabilitySlots, type SalesAppointment,
} from '../api/task3';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner, Field } from '../components/Form';
import { DoctorSelect, PatientPicker, type PickedPatient } from '../components/Pickers';
import { addDays, clinicDayStartIso, clinicLocalToIso, clinicToday, formatClinicTime, formatDate } from '../lib/time';
import { useLoad } from '../lib/useLoad';

/**
 * FR-SCH-003..007, -010 (Sales / Reception and Manager): the day's appointments, booking with a manually entered
 * duration (overlaps allowed), Confirm / Cancel / No Show, reschedule (a new appointment; history kept) and — Sales only —
 * recording the patient's arrival (an event, never a status).
 */
export function AppointmentsPage() {
  const { me, can } = useAuth();
  const zone = me?.clinicTimezone;
  const [params] = useSearchParams();
  const [day, setDay] = useState(() => clinicToday(zone));
  const [doctorFilter, setDoctorFilter] = useState('');
  const from = clinicDayStartIso(day, zone);
  const to = clinicDayStartIso(addDays(day, 1), zone);
  const listUrl = `/api/sales/appointments?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}${doctorFilter ? `&doctorId=${doctorFilter}` : ''}`;
  const { data, error, reload } = useLoad<SalesAppointment[]>(listUrl);

  return (
    <section>
      <h1>المواعيد</h1>
      {can('Scheduling.Appointments.Book') && <BookingForm initialPatientId={params.get('patientId')} onBooked={() => void reload()} />}
      <div className="card inline">
        <label className="field">
          <span>اليوم</span>
          <input type="date" value={day} onChange={(e) => setDay(e.target.value)} />
        </label>
        <label className="field">
          <span>الطبيب</span>
          <DoctorSelect value={doctorFilter} onChange={setDoctorFilter} required={false} />
        </label>
        <p className="hint">{formatDate(day)}</p>
      </div>
      <ErrorBanner error={error} />
      <table>
        <thead>
          <tr>
            <th>الوقت</th>
            <th>المدة</th>
            <th>المريض</th>
            <th>الطبيب</th>
            <th>السبب</th>
            <th>النوع</th>
            <th>الحالة</th>
            <th>الحضور</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {data?.map((a) => (
            <AppointmentRow key={a.id} appointment={a} onChanged={() => void reload()} />
          ))}
        </tbody>
      </table>
      {data && data.length === 0 && <p className="hint">لا توجد مواعيد في هذا اليوم.</p>}
    </section>
  );
}

function AppointmentRow({ appointment: a, onChanged }: { appointment: SalesAppointment; onChanged: () => void }) {
  const { me, can } = useAuth();
  const zone = me?.clinicTimezone;
  const [error, setError] = useState<unknown>(null);
  const [mode, setMode] = useState<'none' | 'cancel' | 'reschedule'>('none');
  const open = isOpenAppointment(a.status);

  async function run(action: () => Promise<unknown>) {
    setError(null);
    try {
      await action();
      setMode('none');
      onChanged();
    } catch (e) {
      setError(e);
    }
  }

  const status = (s: string, reason: string | null = null) =>
    run(() => api.post(`/api/sales/appointments/${a.id}/status`, { status: s, reason }, a.version));

  return (
    <>
      <tr>
        <td>{formatClinicTime(a.startAt, zone)}</td>
        <td>{a.durationMinutes} دقيقة</td>
        <td>
          <Link to={`/patients/${a.patient.id}`}>
            {a.patient.patientCode} — {a.patient.fullName}
          </Link>
          {a.patient.phone && <div className="hint">{a.patient.phone}</div>}
        </td>
        <td>{a.doctor.name}</td>
        <td>
          {a.reason}
          {a.salesNotes && <div className="hint">ملاحظات: {a.salesNotes}</div>}
        </td>
        <td>{appointmentKindLabels[a.kind]}</td>
        <td>
          {appointmentStatusLabels[a.status]}
          {a.previousAppointmentId && <div className="hint">بديل لموعد سابق</div>}
        </td>
        <td>{a.arrivedAt ? `حضر ${formatClinicTime(a.arrivedAt, zone)}` : '—'}</td>
        <td className="actions">
          {open && can('Scheduling.Arrival.Record') && !a.arrivedAt && (
            <button type="button" onClick={() => void run(() => api.post(`/api/sales/appointments/${a.id}/arrival`))}>
              تسجيل الحضور
            </button>
          )}
          {open && can('Scheduling.Arrival.Void') && a.arrivedAt && (
            <button type="button" className="secondary" onClick={() => void run(() => api.post(`/api/sales/appointments/${a.id}/arrival/void`))}>
              إلغاء تسجيل الحضور
            </button>
          )}
          {a.status === 'Scheduled' && can('Scheduling.Appointments.UpdateStatus') && (
            <button type="button" className="secondary" onClick={() => void status('Confirmed')}>
              تأكيد
            </button>
          )}
          {open && can('Scheduling.Appointments.UpdateStatus') && (
            <>
              <button type="button" className="secondary" onClick={() => void status('NoShow')}>
                لم يحضر
              </button>
              <button type="button" className="secondary" onClick={() => setMode('cancel')}>
                إلغاء
              </button>
            </>
          )}
          {open && can('Scheduling.Appointments.Reschedule') && (
            <button type="button" className="secondary" onClick={() => setMode('reschedule')}>
              إعادة جدولة
            </button>
          )}
        </td>
      </tr>
      {(mode !== 'none' || error !== null) && (
        <tr>
          <td colSpan={9}>
            <ErrorBanner error={error} />
            {mode === 'cancel' && <CancelForm onSubmit={(reason) => void status('Cancelled', reason)} onClose={() => setMode('none')} />}
            {mode === 'reschedule' && (
              <RescheduleForm
                appointment={a}
                onSubmit={(body) => void run(() => api.post(`/api/sales/appointments/${a.id}/reschedule`, body, a.version))}
                onClose={() => setMode('none')}
              />
            )}
          </td>
        </tr>
      )}
    </>
  );
}

function CancelForm({ onSubmit, onClose }: { onSubmit: (reason: string) => void; onClose: () => void }) {
  const [reason, setReason] = useState('');
  return (
    <div className="inline">
      <input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="سبب الإلغاء" />
      <button type="button" onClick={() => onSubmit(reason)} disabled={!reason.trim()}>
        تأكيد الإلغاء
      </button>
      <button type="button" className="link" onClick={onClose}>
        رجوع
      </button>
    </div>
  );
}

function RescheduleForm({ appointment, onSubmit, onClose }: {
  appointment: SalesAppointment;
  onSubmit: (body: object) => void;
  onClose: () => void;
}) {
  const { me } = useAuth();
  const [start, setStart] = useState('');
  const [duration, setDuration] = useState(String(appointment.durationMinutes));
  const [doctorId, setDoctorId] = useState(appointment.doctor.id);
  const [rescheduleReason, setRescheduleReason] = useState('');

  return (
    <div className="inline wrap">
      <label className="field">
        <span>الموعد الجديد</span>
        <input type="datetime-local" value={start} onChange={(e) => setStart(e.target.value)} />
      </label>
      <label className="field">
        <span>المدة (دقيقة)</span>
        <input type="number" min={1} max={480} value={duration} onChange={(e) => setDuration(e.target.value)} />
      </label>
      <label className="field">
        <span>الطبيب</span>
        <DoctorSelect value={doctorId} onChange={setDoctorId} />
      </label>
      <label className="field">
        <span>سبب إعادة الجدولة</span>
        <input value={rescheduleReason} onChange={(e) => setRescheduleReason(e.target.value)} />
      </label>
      <button
        type="button"
        disabled={!start || !rescheduleReason.trim()}
        onClick={() => onSubmit({
          startAt: clinicLocalToIso(start, me?.clinicTimezone),
          durationMinutes: Number(duration),
          doctorId,
          reason: null,
          rescheduleReason,
        })}
      >
        حفظ الموعد الجديد
      </button>
      <button type="button" className="link" onClick={onClose}>
        رجوع
      </button>
      <p className="hint">يبقى الموعد القديم في السجل بحالة «أعيدت جدولته» ويُنشأ موعد جديد مرتبط به.</p>
    </div>
  );
}

function BookingForm({ initialPatientId, onBooked }: { initialPatientId: string | null; onBooked: () => void }) {
  const { me } = useAuth();
  const zone = me?.clinicTimezone;
  const [patient, setPatient] = useState<PickedPatient | null>(
    initialPatientId ? { id: initialPatientId, patientCode: '', fullName: 'المريض المختار' } : null,
  );
  const [doctorId, setDoctorId] = useState('');
  const [start, setStart] = useState('');
  const [duration, setDuration] = useState('30');
  const [reason, setReason] = useState('');
  const [salesNotes, setSalesNotes] = useState('');
  const [kind, setKind] = useState<AppointmentKind>('Standard');
  const [error, setError] = useState<unknown>(null);
  const [done, setDone] = useState('');
  const searchUrl = me?.role === 'MANAGER' ? '/api/manager/patients/search' : '/api/sales/patients/search';

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setDone('');
    try {
      await api.post('/api/sales/appointments', {
        patientId: patient?.id,
        doctorId,
        startAt: start ? clinicLocalToIso(start, zone) : null,
        durationMinutes: duration ? Number(duration) : null,
        reason,
        salesNotes: salesNotes.trim() || null,
        kind,
      });
      setDone('تم حجز الموعد.');
      setReason('');
      setSalesNotes('');
      onBooked();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <form className="card" onSubmit={(e) => void submit(e)}>
      <h2>حجز موعد</h2>
      <Field label="المريض" field="patientId" error={error}>
        <PatientPicker searchUrl={searchUrl} value={patient} onChange={setPatient} />
      </Field>
      <Field label="الطبيب" field="doctorId" error={error}>
        <DoctorSelect value={doctorId} onChange={setDoctorId} />
      </Field>
      {doctorId && start && <SlotsHint doctorId={doctorId} day={start.slice(0, 10)} />}
      <Field label="بداية الموعد" field="startAt" error={error}>
        <input type="datetime-local" value={start} onChange={(e) => setStart(e.target.value)} required />
      </Field>
      <Field label="المدة بالدقائق (تُدخل يدوياً: 1–480)" field="durationMinutes" error={error}>
        <input type="number" min={1} max={480} value={duration} onChange={(e) => setDuration(e.target.value)} required />
      </Field>
      <Field label="سبب الزيارة (يظهر للطبيب)" field="reason" error={error}>
        <input value={reason} onChange={(e) => setReason(e.target.value)} required />
      </Field>
      <Field label="ملاحظات المبيعات (لا تظهر للطبيب)" field="salesNotes" error={error}>
        <textarea value={salesNotes} onChange={(e) => setSalesNotes(e.target.value)} />
      </Field>
      <Field label="نوع الموعد" field="kind" error={error}>
        <select value={kind} onChange={(e) => setKind(e.target.value as AppointmentKind)}>
          <option value="Standard">{appointmentKindLabels.Standard}</option>
          <option value="ImplantFollowUp">{appointmentKindLabels.ImplantFollowUp}</option>
        </select>
      </Field>
      <ErrorBanner error={error} />
      {done && <div className="ok">{done}</div>}
      <button type="submit" disabled={!patient}>
        حجز
      </button>
      <p className="hint">يُسمح بتداخل المواعيد لنفس الطبيب. الحجز لا يتطلب استكمال التاريخ المرضي.</p>
    </form>
  );
}

/** Published availability and already-booked ranges of the chosen Doctor on the chosen day (overlaps stay allowed). */
function SlotsHint({ doctorId, day }: { doctorId: string; day: string }) {
  const { me } = useAuth();
  const { data } = useLoad<AvailabilitySlots>(`/api/sales/availability/slots?doctorId=${doctorId}&from=${day}&to=${day}`);
  if (!data) {
    return null;
  }
  return (
    <div className="hint">
      <div>
        الإتاحة المنشورة: {data.ranges.length === 0 ? 'لا توجد' : data.ranges.map((r) => `${r.start.slice(0, 5)}–${r.end.slice(0, 5)}`).join('، ')}
      </div>
      <div>
        محجوز: {data.booked.length === 0 ? 'لا يوجد'
          : data.booked.map((b) => `${formatClinicTime(b.startAt, me?.clinicTimezone)}–${formatClinicTime(b.effectiveEndAt, me?.clinicTimezone)}`).join('، ')}
      </div>
    </div>
  );
}

