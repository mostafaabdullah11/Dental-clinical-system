import { useState, type FormEvent } from 'react';
import { ApiError, api } from '../../api/client';
import { appointmentKindLabels, referralTypeLabels, type AppointmentKind, type ReferralType, type VisitDetail } from '../../api/task3';
import { useAuth } from '../../auth/AuthContext';
import { isDecimalText, toTwoDecimals } from '../../lib/decimal';
import { clinicLocalToIso } from '../../lib/time';
import { ErrorBanner, Field } from '../Form';

/** FR-SCH-008: the next appointment from this Visit, with a manually entered duration. */
export function FollowUpForm({ visitId }: { visitId: string }) {
  const { me } = useAuth();
  const [start, setStart] = useState('');
  const [duration, setDuration] = useState('30');
  const [reason, setReason] = useState('');
  const [kind, setKind] = useState<AppointmentKind>('Standard');
  const [error, setError] = useState<unknown>(null);
  const [done, setDone] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setDone(false);
    try {
      await api.post('/api/doctor/appointments/follow-up', {
        visitId, startAt: clinicLocalToIso(start, me?.clinicTimezone), durationMinutes: Number(duration), reason, kind,
      });
      setDone(true);
      setReason('');
    } catch (e) {
      setError(e);
    }
  }

  return (
    <form className="card" onSubmit={(e) => void submit(e)}>
      <h2>الموعد القادم</h2>
      <div className="inline wrap">
        <Field label="الموعد" field="startAt" error={error}>
          <input type="datetime-local" value={start} onChange={(e) => setStart(e.target.value)} required />
        </Field>
        <Field label="المدة (دقيقة)" field="durationMinutes" error={error}>
          <input type="number" min={1} max={480} value={duration} onChange={(e) => setDuration(e.target.value)} required />
        </Field>
        <Field label="السبب" field="reason" error={error}>
          <input value={reason} onChange={(e) => setReason(e.target.value)} required />
        </Field>
        <Field label="النوع" field="kind" error={error}>
          <select value={kind} onChange={(e) => setKind(e.target.value as AppointmentKind)}>
            <option value="Standard">{appointmentKindLabels.Standard}</option>
            <option value="ImplantFollowUp">{appointmentKindLabels.ImplantFollowUp}</option>
          </select>
        </Field>
      </div>
      <ErrorBanner error={error} />
      {done && <div className="ok">تم حجز الموعد القادم ويظهر للاستقبال.</div>}
      <button type="submit">حجز الموعد القادم</button>
    </form>
  );
}

/** FR-REF-001: Sales sees the destination, reason and instructions; the notes stay with Doctors and the Manager. */
export function ReferralForm({ visitId }: { visitId: string }) {
  const [type, setType] = useState<ReferralType>('Surgery');
  const [reason, setReason] = useState('');
  const [notes, setNotes] = useState('');
  const [instructions, setInstructions] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [done, setDone] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setDone(false);
    try {
      await api.post(`/api/doctor/visits/${visitId}/referrals`, {
        referralType: type, reason, notes: notes.trim() || null, followUpInstructions: instructions.trim() || null,
      });
      setDone(true);
      setReason('');
      setNotes('');
      setInstructions('');
    } catch (e) {
      setError(e);
    }
  }

  return (
    <form className="card" onSubmit={(e) => void submit(e)}>
      <h2>تحويل المريض</h2>
      <Field label="جهة / نوع التحويل" field="referralType" error={error}>
        <select value={type} onChange={(e) => setType(e.target.value as ReferralType)}>
          {(Object.keys(referralTypeLabels) as ReferralType[]).map((t) => (
            <option key={t} value={t}>
              {referralTypeLabels[t]}
            </option>
          ))}
        </select>
      </Field>
      <Field label="سبب التحويل (يظهر للمبيعات)" field="reason" error={error}>
        <input value={reason} onChange={(e) => setReason(e.target.value)} required />
      </Field>
      <Field label="تعليمات المتابعة (تظهر للمبيعات)" field="followUpInstructions" error={error}>
        <input value={instructions} onChange={(e) => setInstructions(e.target.value)} />
      </Field>
      <Field label="ملاحظات الطبيب (لا تظهر للمبيعات)" field="notes" error={error}>
        <textarea value={notes} onChange={(e) => setNotes(e.target.value)} />
      </Field>
      <ErrorBanner error={error} />
      {done && <div className="ok">تم التحويل وإشعار المبيعات / الاستقبال.</div>}
      <button type="submit">إرسال التحويل</button>
    </form>
  );
}

/** FR-ORT-001 / -002 (Task 3): area, appliance and the Doctor's initial terms — stored only; no installments yet. */
export function OrthoCaseForm({ visitId, onCreated }: { visitId: string; onCreated: () => void }) {
  const [open, setOpen] = useState(false);
  const [area, setArea] = useState('UpperArch');
  const [appliance, setAppliance] = useState('');
  const [gross, setGross] = useState('');
  const [deposit, setDeposit] = useState('');
  const [months, setMonths] = useState('12');
  const [error, setError] = useState<unknown>(null);

  if (!open) {
    return (
      <button type="button" className="secondary" onClick={() => setOpen(true)}>
        إضافة حالة تقويم
      </button>
    );
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    if (!isDecimalText(gross) || !isDecimalText(deposit)) {
      setError(new ApiError(400, 'validation_failed'));
      return;
    }
    try {
      await api.post(`/api/doctor/visits/${visitId}/orthodontic-cases`, {
        area, applianceType: area === 'Appliance' ? appliance : null, grossTotalPrice: toTwoDecimals(gross), depositAmount: toTwoDecimals(deposit),
        numberOfMonths: Number(months),
      });
      setOpen(false);
      onCreated();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <form className="card" onSubmit={(e) => void submit(e)}>
      <h2>حالة تقويم</h2>
      <Field label="المنطقة" field="area" error={error}>
        <select value={area} onChange={(e) => setArea(e.target.value)}>
          <option value="UpperArch">الفك العلوي</option>
          <option value="LowerArch">الفك السفلي</option>
          <option value="Appliance">جهاز</option>
        </select>
      </Field>
      {area === 'Appliance' && (
        <Field label="نوع الجهاز" field="applianceType" error={error}>
          <input value={appliance} onChange={(e) => setAppliance(e.target.value)} required />
        </Field>
      )}
      <Field label="إجمالي التقويم (ج.م)" field="grossTotalPrice" error={error}>
        <input inputMode="decimal" value={gross} onChange={(e) => setGross(e.target.value)} required />
      </Field>
      <Field label="المقدم (ج.م)" field="depositAmount" error={error}>
        <input inputMode="decimal" value={deposit} onChange={(e) => setDeposit(e.target.value)} required />
      </Field>
      <Field label="عدد الأشهر" field="numberOfMonths" error={error}>
        <input type="number" min={1} value={months} onChange={(e) => setMonths(e.target.value)} required />
      </Field>
      <ErrorBanner error={error} />
      <p className="hint">تُنشأ الأقساط عند إنهاء الزيارة وتثبيت المستحق (المرحلة الرابعة). يجب ألا يتجاوز المقدم الإجمالي بعد الخصم.</p>
      <button type="submit">حفظ حالة التقويم</button>
    </form>
  );
}

/** FR-CLN-014: the own Doctor's controlled correction of the notes of a finalized Visit (audited; never reopened). */
export function NotesCorrection({ visit, onCorrected }: { visit: VisitDetail; onCorrected: (v: VisitDetail) => void }) {
  const [notes, setNotes] = useState(visit.clinicalNotes ?? '');
  const [reason, setReason] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [open, setOpen] = useState(false);

  if (!open) {
    return (
      <button type="button" className="secondary" onClick={() => setOpen(true)}>
        تصحيح ملاحظات الزيارة
      </button>
    );
  }

  async function submit() {
    setError(null);
    try {
      onCorrected(await api.post<VisitDetail>(`/api/doctor/visits/${visit.id}/corrections`,
        { recordType: 'VisitNotes', recordId: visit.id, clinicalNotes: notes, reason: reason.trim() || null }));
      setOpen(false);
    } catch (e) {
      setError(e);
    }
  }

  return (
    <div className="card">
      <h2>تصحيح ملاحظات زيارة منتهية</h2>
      <textarea rows={4} value={notes} onChange={(e) => setNotes(e.target.value)} />
      <label className="field">
        <span>سبب التصحيح</span>
        <input value={reason} onChange={(e) => setReason(e.target.value)} />
      </label>
      <ErrorBanner error={error} />
      <button type="button" onClick={() => void submit()}>
        حفظ التصحيح
      </button>
    </div>
  );
}
