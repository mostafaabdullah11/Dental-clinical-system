import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ApiError, api } from '../api/client';
import { label, visitStatusLabels, type CaseSummary, type OptionField, type ProcedureType, type VisitDetail } from '../api/task3';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner, Field } from '../components/Form';
import { DiscountEditor } from '../components/visit/DiscountEditor';
import { PrescriptionPanel } from '../components/visit/PrescriptionPanel';
import { TreatmentCard } from '../components/visit/TreatmentCard';
import { FollowUpForm, NotesCorrection, OrthoCaseForm, ReferralForm } from '../components/visit/VisitExtras';
import { isDecimalText, toTwoDecimals } from '../lib/decimal';
import { formatAge } from '../lib/patients';
import { formatClinicDateTime } from '../lib/time';
import { useLoad } from '../lib/useLoad';

/**
 * The Doctor's Visit workspace (FR-CLN-001..015): notes and consultation fee, treatments from the catalog (steps,
 * canals, implant), price and fixed-EGP discount, Fixed Lab and ortho initial terms (stored only; no charge before
 * Task 4), prescriptions, next appointment, referral, finalization in one transaction, and the own-Doctor correction
 * path afterwards. Contact data does not exist in any DTO this page reads.
 */
export function VisitPage() {
  const { id = '' } = useParams();
  const [visit, setVisit] = useState<VisitDetail | null>(null);
  const [error, setError] = useState<unknown>(null);

  const reload = useCallback(async () => {
    try {
      setError(null);
      setVisit(await api.get<VisitDetail>(`/api/doctor/visits/${id}`));
    } catch (e) {
      setError(e);
    }
  }, [id]);

  useEffect(() => {
    void reload();
  }, [reload]);

  if (!visit) {
    return <ErrorBanner error={error} />;
  }

  return <VisitWorkspace visit={visit} setVisit={setVisit} reload={reload} />;
}

function VisitWorkspace({ visit, setVisit, reload }: { visit: VisitDetail; setVisit: (v: VisitDetail) => void; reload: () => Promise<void> }) {
  const { me } = useAuth();
  const zone = me?.clinicTimezone;
  const editable = visit.canEdit;
  const [error, setError] = useState<unknown>(null);
  const [confirming, setConfirming] = useState(false);

  async function finalize() {
    setConfirming(false);
    setError(null);
    try {
      setVisit(await api.post<VisitDetail>(`/api/doctor/visits/${visit.id}/finalize`, undefined, visit.version));
    } catch (e) {
      setError(e);
      void reload();
    }
  }

  return (
    <section>
      <h1>
        زيارة — <Link to={`/patients/${visit.patient.id}`}>{visit.patient.patientCode} — {visit.patient.fullName}</Link>
      </h1>
      <div className="card inline wrap">
        <span>العمر: {formatAge(visit.patient.ageDisplay)}</span>
        <span>الطبيب: {visit.doctor.name}</span>
        <span>بدأت: {formatClinicDateTime(visit.startedAt, zone)}</span>
        <span>
          الحالة: <strong>{visitStatusLabels[visit.status]}</strong>
        </span>
        {visit.finalizedAt && <span>انتهت: {formatClinicDateTime(visit.finalizedAt, zone)}</span>}
        {editable && !confirming && (
          <button type="button" onClick={() => setConfirming(true)}>
            إنهاء الزيارة
          </button>
        )}
        {editable && confirming && (
          <span className="actions" role="alertdialog" aria-label="تأكيد إنهاء الزيارة">
            <span>إنهاء الزيارة نهائي: لن يمكن تعديلها بعد ذلك إلا بالتصحيح المراقَب. هل تريد المتابعة؟</span>
            <button type="button" onClick={() => void finalize()}>
              تأكيد الإنهاء
            </button>
            <button type="button" onClick={() => setConfirming(false)}>
              رجوع
            </button>
          </span>
        )}
      </div>
      <ErrorBanner error={error} />

      <NotesAndFee visit={visit} onSaved={setVisit} />
      {visit.consultationLine && (
        <div className="card">
          <h2>كشف</h2>
          <p>
            رسوم الكشف: {visit.consultationLine.fee} ج.م — الخصم: {visit.consultationLine.discount} ج.م
          </p>
          {editable && (
            <DiscountEditor
              url={`/api/doctor/pricing-lines/${visit.consultationLine.lineId}/discount`}
              current={visit.consultationLine.discount}
              entrySeq={visit.consultationLine.entrySeq}
              onSaved={() => void reload()}
            />
          )}
        </div>
      )}

      <h2>العلاجات</h2>
      {visit.treatments.length === 0 && <p className="hint">لم تُسجل علاجات بعد.</p>}
      {visit.treatments.map((t) => (
        <TreatmentCard key={t.recordId} visit={visit} record={t} onChanged={setVisit} reload={reload} />
      ))}
      {editable && <AddTreatment visit={visit} onAdded={setVisit} />}
      {editable && <OrthoCaseForm visitId={visit.id} onCreated={() => void reload()} />}

      <PrescriptionPanel visit={visit} />
      {visit.isOwn && <FollowUpForm visitId={visit.id} />}
      {visit.isOwn && <ReferralForm visitId={visit.id} />}
      {visit.isOwn && visit.status === 'Finalized' && <NotesCorrection visit={visit} onCorrected={setVisit} />}
    </section>
  );
}

function NotesAndFee({ visit, onSaved }: { visit: VisitDetail; onSaved: (v: VisitDetail) => void }) {
  const [notes, setNotes] = useState(visit.clinicalNotes ?? '');
  const [fee, setFee] = useState(visit.consultationFee ?? '');
  const [error, setError] = useState<unknown>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    setNotes(visit.clinicalNotes ?? '');
    setFee(visit.consultationFee ?? '');
  }, [visit.clinicalNotes, visit.consultationFee]);

  async function save(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setSaved(false);
    if (fee.trim() && !isDecimalText(fee)) {
      setError(new ApiError(400, 'validation_failed'));
      return;
    }
    try {
      onSaved(await api.put<VisitDetail>(`/api/doctor/visits/${visit.id}`,
        { clinicalNotes: notes.trim() || null, consultationFee: fee.trim() ? toTwoDecimals(fee) : null }, visit.version));
      setSaved(true);
    } catch (e) {
      setError(e);
    }
  }

  if (!visit.canEdit) {
    return (
      <div className="card">
        <h2>ملاحظات الزيارة</h2>
        <p className="prewrap">{visit.clinicalNotes ?? '—'}</p>
        <p>رسوم الكشف: {visit.consultationFee ? `${visit.consultationFee} ج.م` : 'لا يوجد'}</p>
      </div>
    );
  }

  return (
    <form className="card" onSubmit={(e) => void save(e)}>
      <h2>ملاحظات الزيارة ورسوم الكشف</h2>
      <Field label="الملاحظات السريرية (لا تظهر للمبيعات)" field="clinicalNotes" error={error}>
        <textarea rows={4} value={notes} onChange={(e) => setNotes(e.target.value)} />
      </Field>
      <Field label="رسوم الكشف بالجنيه (اختياري)" field="consultationFee" error={error}>
        <input inputMode="decimal" value={fee} onChange={(e) => setFee(e.target.value)} placeholder="مثال: 200" />
      </Field>
      <ErrorBanner error={error} />
      {saved && <div className="ok">تم الحفظ.</div>}
      <button type="submit">حفظ</button>
    </form>
  );
}

/** New case from the catalog (option fields rendered from option_definition) or the continuation of an existing case. */
function AddTreatment({ visit, onAdded }: { visit: VisitDetail; onAdded: (v: VisitDetail) => void }) {
  const { data: catalog } = useLoad<ProcedureType[]>('/api/doctor/procedure-types');
  const { data: cases } = useLoad<CaseSummary[]>(`/api/doctor/patients/${visit.patient.id}/treatment-cases`);
  const [code, setCode] = useState('');
  const [teeth, setTeeth] = useState('');
  const [details, setDetails] = useState<Record<string, string>>({});
  const [error, setError] = useState<unknown>(null);
  const type = catalog?.find((t) => t.code === code);
  const inVisit = new Set(visit.treatments.map((t) => t.case.id));
  const continuable = (cases ?? []).filter((c) => (c.status === 'Active' || c.status === 'Planned') && !inVisit.has(c.id) && c.procedureCode !== 'Orthodontics');

  async function add(body: object) {
    setError(null);
    try {
      onAdded(await api.post<VisitDetail>(`/api/doctor/visits/${visit.id}/treatments`, body));
      setCode('');
      setTeeth('');
      setDetails({});
    } catch (e) {
      setError(e);
    }
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    const teethList = teeth.split(/[\s,،]+/).map((t) => t.trim()).filter(Boolean);
    const cleaned = Object.fromEntries(Object.entries(details).filter(([, v]) => v.trim() !== ''));
    void add({ procedureCode: code, teeth: teethList.length ? teethList : null, area: null, details: Object.keys(cleaned).length ? cleaned : null });
  }

  return (
    <div className="card">
      <h2>إضافة علاج</h2>
      <form onSubmit={submit}>
        <Field label="نوع الإجراء" field="procedureCode" error={error}>
          <select value={code} onChange={(e) => { setCode(e.target.value); setDetails({}); }} required>
            <option value="">— اختر —</option>
            {catalog?.filter((t) => t.code !== 'Orthodontics').map((t) => (
              <option key={t.code} value={t.code}>
                {t.name}
              </option>
            ))}
          </select>
        </Field>
        {type && type.optionDefinition.teeth !== 'none' && (
          <Field label={`الأسنان (ترقيم FDI${type.optionDefinition.teeth === 'single' ? '، سن واحد' : ''})`} field="teeth" error={error}>
            <input value={teeth} onChange={(e) => setTeeth(e.target.value)} placeholder="مثال: 16 أو 11، 21" required={type.optionDefinition.teeth !== 'optional'} />
          </Field>
        )}
        {type?.optionDefinition.fields.filter((f) => visible(f, details)).map((f) => (
          <OptionInput key={f.name} field={f} value={details[f.name] ?? ''} onChange={(v) => setDetails({ ...details, [f.name]: v })} />
        ))}
        <ErrorBanner error={error} />
        <button type="submit" disabled={!code}>
          إضافة
        </button>
      </form>
      {continuable.length > 0 && (
        <div className="subsection">
          <h3>متابعة حالة قائمة</h3>
          {continuable.map((c) => (
            <div key={c.id} className="inline">
              <span>
                {c.procedureName} {c.teeth.length ? `(${c.teeth.join('، ')})` : ''} — بدأها {c.startedBy.name}
              </span>
              <button type="button" className="secondary" onClick={() => void add({ caseId: c.id })}>
                تسجيل عمل في هذه الزيارة
              </button>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function visible(field: OptionField, values: Record<string, string>): boolean {
  return !field.allowedWhen || values[field.allowedWhen.field] === field.allowedWhen.equals;
}

export function OptionInput({ field, value, onChange }: { field: OptionField; value: string; onChange: (v: string) => void }) {
  const required = field.required === true;
  return (
    <label className="field">
      <span>{label(field.name)}</span>
      {field.kind === 'enum' ? (
        <select value={value} onChange={(e) => onChange(e.target.value)} required={required}>
          <option value="">— اختر —</option>
          {field.values?.map((v) => (
            <option key={v} value={v}>
              {label(v)}
            </option>
          ))}
        </select>
      ) : (
        <input value={value} onChange={(e) => onChange(e.target.value)} required={required} />
      )}
    </label>
  );
}
