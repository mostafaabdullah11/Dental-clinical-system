import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import {
  feedbackSourceLabels, feedbackStateLabels,
  type FeedbackQueueItem, type FeedbackRequestState, type FeedbackResult,
} from '../api/task3';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner, Field } from '../components/Form';
import { DoctorSelect, PatientPicker, type PickedPatient } from '../components/Pickers';
import { addDays, clinicToday, formatClinicDateTime } from '../lib/time';
import { useLoad } from '../lib/useLoad';

/**
 * FR-FDB-002..004 / -008 / -009 (Sales / Reception and Manager): the feedback queue with the derived state, preparing
 * the unique link (shown once), marking it sent, and recording feedback the patient gives directly. No rating or comment
 * is ever shown here — results are the Manager's only. Links never expire.
 */
export function FeedbackQueuePage() {
  const { me } = useAuth();
  const [status, setStatus] = useState<FeedbackRequestState | ''>('Pending');
  const { data, error, reload } = useLoad<FeedbackQueueItem[]>(`/api/sales/feedback-requests${status ? `?status=${status}` : ''}`);

  return (
    <section>
      <h1>طلبات التقييم</h1>
      <div className="card inline">
        <label className="field">
          <span>الحالة</span>
          <select value={status} onChange={(e) => setStatus(e.target.value as FeedbackRequestState | '')}>
            <option value="">الكل</option>
            {(Object.keys(feedbackStateLabels) as FeedbackRequestState[]).map((s) => (
              <option key={s} value={s}>
                {feedbackStateLabels[s]}
              </option>
            ))}
          </select>
        </label>
      </div>
      <ErrorBanner error={error} />
      <table>
        <thead>
          <tr>
            <th>التاريخ</th>
            <th>المريض</th>
            <th>الطبيب</th>
            <th>الحالة</th>
            <th>الإرسال</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {data?.map((r) => (
            <FeedbackRow key={r.id} item={r} zone={me?.clinicTimezone} onChanged={() => void reload()} />
          ))}
        </tbody>
      </table>
      <RecordFeedbackForm />
    </section>
  );
}

function FeedbackRow({ item: r, zone, onChanged }: { item: FeedbackQueueItem; zone: string | null | undefined; onChanged: () => void }) {
  const [url, setUrl] = useState<string | null>(null);
  const [channel, setChannel] = useState('واتساب');
  const [error, setError] = useState<unknown>(null);

  async function prepare() {
    setError(null);
    try {
      setUrl((await api.post<{ url: string }>(`/api/sales/feedback-requests/${r.id}/link`)).url);
    } catch (e) {
      setError(e);
    }
  }

  async function markSent() {
    setError(null);
    try {
      await api.post(`/api/sales/feedback-requests/${r.id}/mark-sent`, { channel });
      setUrl(null);
      onChanged();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <>
      <tr>
        <td>{formatClinicDateTime(r.createdAt, zone)}</td>
        <td>
          <Link to={`/patients/${r.patient.id}`}>
            {r.patient.patientCode} — {r.patient.fullName}
          </Link>
          <div className="hint">{[r.patient.phone, r.patient.whatsapp].filter(Boolean).join(' / ')}</div>
        </td>
        <td>{r.doctor.name}</td>
        <td>{feedbackStateLabels[r.status]}</td>
        <td>{r.linkSentAt ? `${formatClinicDateTime(r.linkSentAt, zone)} — ${r.deliveryChannel ?? ''}` : '—'}</td>
        <td className="actions">
          {r.status !== 'Submitted' && (
            <button type="button" className="secondary" onClick={() => void prepare()}>
              {r.linkPrepared ? 'إنشاء رابط جديد' : 'إنشاء الرابط'}
            </button>
          )}
        </td>
      </tr>
      {(url || error !== null) && (
        <tr>
          <td colSpan={6}>
            <ErrorBanner error={error} />
            {url && (
              <div className="inline wrap">
                <input readOnly value={url} className="wide" onFocus={(e) => e.target.select()} />
                <span className="hint">يظهر الرابط مرة واحدة فقط. أي رابط سابق لهذا الطلب توقف عن العمل.</span>
                <input value={channel} onChange={(e) => setChannel(e.target.value)} placeholder="وسيلة الإرسال" />
                <button type="button" onClick={() => void markSent()} disabled={!channel.trim()}>
                  تم الإرسال
                </button>
              </div>
            )}
          </td>
        </tr>
      )}
    </>
  );
}

/** FR-FDB-008 / -009: with a Visit it links that Visit's request (which becomes Submitted); without one it is general. */
function RecordFeedbackForm() {
  const { me } = useAuth();
  const [patient, setPatient] = useState<PickedPatient | null>(null);
  const [doctorId, setDoctorId] = useState('');
  const [visitId, setVisitId] = useState('');
  const [rating, setRating] = useState('5');
  const [comment, setComment] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [done, setDone] = useState(false);
  const searchUrl = me?.role === 'MANAGER' ? '/api/manager/patients/search' : '/api/sales/patients/search';
  const { data: requests } = useLoad<FeedbackQueueItem[]>('/api/sales/feedback-requests');
  const visits = (requests ?? []).filter((r) => r.patient.id === patient?.id && r.doctor.id === doctorId && r.status !== 'Submitted');

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setDone(false);
    try {
      await api.post('/api/sales/feedback-entries', {
        patientId: patient?.id, doctorId, visitId: visitId || null, rating: Number(rating), comment: comment.trim() || null,
      });
      setDone(true);
      setComment('');
      setVisitId('');
    } catch (e) {
      setError(e);
    }
  }

  return (
    <form className="card" onSubmit={(e) => void submit(e)}>
      <h2>تسجيل تقييم أبلغ به المريض</h2>
      <Field label="المريض" field="patientId" error={error}>
        <PatientPicker searchUrl={searchUrl} value={patient} onChange={setPatient} />
      </Field>
      <Field label="الطبيب" field="doctorId" error={error}>
        <DoctorSelect value={doctorId} onChange={setDoctorId} />
      </Field>
      <Field label="الزيارة (اختياري)" field="visitId" error={error}>
        <select value={visitId} onChange={(e) => setVisitId(e.target.value)}>
          <option value="">تقييم عام (بدون زيارة)</option>
          {visits.map((v) => (
            <option key={v.visitId} value={v.visitId}>
              زيارة {formatClinicDateTime(v.createdAt, me?.clinicTimezone)}
            </option>
          ))}
        </select>
      </Field>
      <Field label="التقييم (1–5)" field="rating" error={error}>
        <select value={rating} onChange={(e) => setRating(e.target.value)}>
          {[5, 4, 3, 2, 1].map((n) => (
            <option key={n} value={n}>
              {'★'.repeat(n)}
            </option>
          ))}
        </select>
      </Field>
      <Field label="التعليق" field="comment" error={error}>
        <textarea value={comment} onChange={(e) => setComment(e.target.value)} />
      </Field>
      <ErrorBanner error={error} />
      {done && <div className="ok">تم تسجيل التقييم.</div>}
      <button type="submit" disabled={!patient || !doctorId}>
        حفظ التقييم
      </button>
    </form>
  );
}

/** FR-FDB-011 (Manager only): ratings and comments of both sources, filtered by Doctor, date range and source. */
export function FeedbackResultsPage() {
  const { me } = useAuth();
  const zone = me?.clinicTimezone;
  const [doctorId, setDoctorId] = useState('');
  const [from, setFrom] = useState(() => addDays(clinicToday(zone), -30));
  const [to, setTo] = useState(() => clinicToday(zone));
  const [source, setSource] = useState('');
  const query = new URLSearchParams({ from, to, ...(doctorId ? { doctorId } : {}), ...(source ? { source } : {}) });
  const { data, error } = useLoad<FeedbackResult[]>(`/api/manager/feedback?${query.toString()}`);

  return (
    <section>
      <h1>نتائج تقييمات المرضى</h1>
      <div className="card inline wrap">
        <label className="field">
          <span>الطبيب</span>
          <DoctorSelect value={doctorId} onChange={setDoctorId} required={false} />
        </label>
        <label className="field">
          <span>من</span>
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label className="field">
          <span>إلى</span>
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
        <label className="field">
          <span>المصدر</span>
          <select value={source} onChange={(e) => setSource(e.target.value)}>
            <option value="">الكل</option>
            <option value="PatientWeb">{feedbackSourceLabels.PatientWeb}</option>
            <option value="SalesEntered">{feedbackSourceLabels.SalesEntered}</option>
          </select>
        </label>
      </div>
      <ErrorBanner error={error} />
      <table>
        <thead>
          <tr>
            <th>التاريخ</th>
            <th>الطبيب</th>
            <th>المريض</th>
            <th>التقييم</th>
            <th>التعليق</th>
            <th>المصدر</th>
          </tr>
        </thead>
        <tbody>
          {data?.map((f) => (
            <tr key={f.id}>
              <td>{formatClinicDateTime(f.recordedAt, zone)}</td>
              <td>{f.doctor.name}</td>
              <td>
                {f.patient.patientCode} — {f.patient.fullName}
              </td>
              <td aria-label={`${f.rating} من 5`}>{'★'.repeat(f.rating)}</td>
              <td className="prewrap">{f.comment ?? '—'}</td>
              <td>{feedbackSourceLabels[f.source]}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {data && data.length === 0 && <p className="hint">لا توجد تقييمات في هذه الفترة.</p>}
    </section>
  );
}
