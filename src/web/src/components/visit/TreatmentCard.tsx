import { useEffect, useState } from 'react';
import { ApiError, api } from '../../api/client';
import {
  caseStatusLabels, label, stepLabels,
  type CaseSummary, type FixedLab, type OrthoCase, type TreatmentRecord, type VisitDetail,
} from '../../api/task3';
import { isDecimalText, toTwoDecimals } from '../../lib/decimal';
import { ErrorBanner } from '../Form';
import { DiscountEditor } from './DiscountEditor';

const invalid = () => new ApiError(400, 'validation_failed');

/** One treatment record of the Visit: case details, steps, canals, implant, own pricing and the Fixed Lab entry. */
export function TreatmentCard({ visit, record, onChanged, reload }: {
  visit: VisitDetail;
  record: TreatmentRecord;
  onChanged: (v: VisitDetail) => void;
  reload: () => Promise<void>;
}) {
  const c = record.case;
  const editable = visit.canEdit && record.doctor.id === visit.doctor.id;
  const [error, setError] = useState<unknown>(null);

  async function run(action: () => Promise<VisitDetail>) {
    setError(null);
    try {
      onChanged(await action());
    } catch (e) {
      setError(e);
    }
  }

  return (
    <div className="card treatment">
      <h3>
        {c.procedureName}
        {c.teeth.length > 0 && ` — الأسنان: ${c.teeth.join('، ')}`}
      </h3>
      <p className="hint">
        الحالة: {caseStatusLabels[c.status]} — بدأها: {c.startedBy.name} — نموذج المعمل: {c.labModel === 'Fixed' ? 'ثابت' : 'عام'}
        {!record.startedInThisVisit && ' — متابعة لحالة بدأت في زيارة سابقة'}
      </p>
      {Object.keys(c.details ?? {}).length > 0 && (
        <ul className="details">
          {Object.entries(c.details).map(([k, v]) => (
            <li key={k}>
              {label(k)}: {label(v)}
            </li>
          ))}
        </ul>
      )}
      <ErrorBanner error={error} />

      {editable && <CaseStatus visitId={visit.id} caseRow={c} onChanged={onChanged} />}

      {c.procedureCode === 'RootCanal' && (
        <RootCanal visit={visit} record={record} editable={editable} run={run} />
      )}
      {c.procedureCode === 'Implant' && (
        <ImplantForm visitId={visit.id} record={record} editable={editable} run={run} />
      )}
      {c.procedureCode === 'Orthodontics' && <OrthoTerms caseId={c.id} />}

      {editable && <Pricing record={record} onChanged={onChanged} reload={reload} />}
      {!editable && record.ownPricing && (
        <p>
          السعر: {record.ownPricing.price} ج.م — الخصم: {record.ownPricing.discount} ج.م
        </p>
      )}
      {c.labModel === 'Fixed' && record.ownPricing && <FixedLabEntry caseId={c.id} editable={visit.canEdit} />}
    </div>
  );
}

function CaseStatus({ visitId, caseRow, onChanged }: { visitId: string; caseRow: CaseSummary; onChanged: (v: VisitDetail) => void }) {
  const [error, setError] = useState<unknown>(null);
  async function change(status: string) {
    setError(null);
    try {
      onChanged(await api.put<VisitDetail>(`/api/doctor/visits/${visitId}/cases/${caseRow.id}/status`, { status }, caseRow.version));
    } catch (e) {
      setError(e);
    }
  }
  return (
    <div className="inline">
      <label className="field">
        <span>حالة العلاج</span>
        <select value={caseRow.status} onChange={(e) => void change(e.target.value)}>
          {(['Planned', 'Active', 'Completed', 'Cancelled'] as const).map((s) => (
            <option key={s} value={s}>
              {caseStatusLabels[s]}
            </option>
          ))}
        </select>
      </label>
      <ErrorBanner error={error} />
    </div>
  );
}

function RootCanal({ visit, record, editable, run }: {
  visit: VisitDetail;
  record: TreatmentRecord;
  editable: boolean;
  run: (action: () => Promise<VisitDetail>) => Promise<void>;
}) {
  const caseId = record.case.id;
  const latest = new Map<number, string>();
  record.measurements.forEach((m) => latest.set(m.canalNumber, m.lengthMm));
  const [canals, setCanals] = useState<{ canalNumber: number; lengthMm: string }[]>(
    [...latest.entries()].map(([canalNumber, lengthMm]) => ({ canalNumber, lengthMm })),
  );
  const hasPostAndCore = record.steps.some((s) => s.stepCode === 'PostAndCore');
  const complete = (code: string) => run(() => api.post<VisitDetail>(`/api/doctor/visits/${visit.id}/cases/${caseId}/steps/${code}/complete`));

  function saveCanals() {
    if (canals.some((c) => c.canalNumber < 1 || c.canalNumber > 5 || !isDecimalText(c.lengthMm))) {
      return run(() => Promise.reject(invalid()));
    }
    return run(() => api.put<VisitDetail>(`/api/doctor/visits/${visit.id}/cases/${caseId}/canal-measurements`,
      { canals: canals.map((c) => ({ canalNumber: c.canalNumber, lengthMm: toTwoDecimals(c.lengthMm) })) }));
  }

  return (
    <div className="subsection">
      <h4>خطوات علاج العصب</h4>
      <ul>
        {record.steps.map((s) => (
          <li key={s.id}>
            {stepLabels[s.stepCode] ?? s.stepCode}: {s.status === 'Completed' ? `تمت (${s.completedBy?.name ?? ''})` : 'لم تتم'}
            {editable && s.status === 'Pending' && (
              <button type="button" className="link" onClick={() => void complete(s.stepCode)}>
                إتمام
              </button>
            )}
          </li>
        ))}
      </ul>
      {editable && !hasPostAndCore && (
        <button type="button" className="secondary" onClick={() => void complete('PostAndCore')}>
          إضافة وإتمام الوتد والقلب
        </button>
      )}
      {!editable && visit.isOwn && visit.status === 'Finalized' && <RootCanalCorrections visit={visit} record={record} run={run} />}
      <h4>أطوال القنوات (مم)</h4>
      {record.measurements.length > 0 && (
        <p className="hint">
          السجل: {record.measurements.map((m) => `قناة ${m.canalNumber}: ${m.lengthMm}`).join('، ')}
        </p>
      )}
      {editable && (
        <>
          {canals.map((c, i) => (
            <div key={i} className="inline">
              <select value={c.canalNumber} onChange={(e) => setCanals(canals.map((x, j) => (j === i ? { ...x, canalNumber: Number(e.target.value) } : x)))}>
                {[1, 2, 3, 4, 5].map((n) => (
                  <option key={n} value={n}>
                    قناة {n}
                  </option>
                ))}
              </select>
              <input inputMode="decimal" value={c.lengthMm} onChange={(e) => setCanals(canals.map((x, j) => (j === i ? { ...x, lengthMm: e.target.value } : x)))} />
              <button type="button" className="link danger" onClick={() => setCanals(canals.filter((_, j) => j !== i))}>
                حذف
              </button>
            </div>
          ))}
          <div className="actions">
            <button type="button" className="secondary" disabled={canals.length >= 5}
              onClick={() => setCanals([...canals, { canalNumber: canals.length + 1, lengthMm: '' }])}>
              إضافة قناة
            </button>
            <button type="button" onClick={() => void saveCanals()} disabled={canals.length === 0}>
              حفظ القياسات
            </button>
          </div>
        </>
      )}
    </div>
  );
}

/** FR-CLN-014: the own Doctor's audited correction of a step or canal recorded in this finalized Visit. */
function RootCanalCorrections({ visit, record, run }: {
  visit: VisitDetail;
  record: TreatmentRecord;
  run: (action: () => Promise<VisitDetail>) => Promise<void>;
}) {
  const [lengths, setLengths] = useState<Record<string, string>>({});
  const [reason, setReason] = useState('');
  const correct = (body: object) => run(() => api.post<VisitDetail>(`/api/doctor/visits/${visit.id}/corrections`, { ...body, reason: reason.trim() || null }));
  const steps = record.steps.filter((s) => s.status === 'Completed' && s.completedInVisitId === visit.id);
  const canals = record.measurements.filter((m) => m.recordedInVisitId === visit.id);
  if (steps.length === 0 && canals.length === 0) {
    return null;
  }
  return (
    <div className="subsection">
      <h4>تصحيح (زيارة منتهية)</h4>
      <label className="field">
        <span>سبب التصحيح</span>
        <input value={reason} onChange={(e) => setReason(e.target.value)} />
      </label>
      {steps.map((s) => (
        <div key={s.id} className="inline">
          <span>{stepLabels[s.stepCode] ?? s.stepCode}</span>
          <button type="button" className="link" onClick={() => void correct({ recordType: 'TreatmentStep', recordId: s.id, stepStatus: 'Pending' })}>
            إرجاع إلى «لم تتم»
          </button>
        </div>
      ))}
      {canals.map((m) => (
        <div key={m.id} className="inline">
          <span>قناة {m.canalNumber}: {m.lengthMm} مم</span>
          <input inputMode="decimal" value={lengths[m.id] ?? ''} onChange={(e) => setLengths({ ...lengths, [m.id]: e.target.value })} placeholder="الطول الصحيح" />
          <button
            type="button"
            className="link"
            disabled={!isDecimalText(lengths[m.id] ?? '')}
            onClick={() => void correct({ recordType: 'RootCanalMeasurement', recordId: m.id, lengthMm: toTwoDecimals(lengths[m.id] ?? '') })}
          >
            تصحيح القياس
          </button>
        </div>
      ))}
    </div>
  );
}

function ImplantForm({ visitId, record, editable, run }: {
  visitId: string;
  record: TreatmentRecord;
  editable: boolean;
  run: (action: () => Promise<VisitDetail>) => Promise<void>;
}) {
  const current = record.implant;
  const [boneGraft, setBoneGraft] = useState(current?.hasBoneGraft ?? false);
  const [membrane, setMembrane] = useState(current?.hasMembrane ?? false);
  const [procedureDate, setProcedureDate] = useState(current?.procedureDate ?? '');
  const [healing, setHealing] = useState(String(current?.healingMonths ?? 3));

  if (!editable) {
    return current ? (
      <p>
        طعم عظمي: {current.hasBoneGraft ? 'نعم' : 'لا'} — غشاء: {current.hasMembrane ? 'نعم' : 'لا'} — مدة الالتئام: {current.healingMonths} شهر
      </p>
    ) : null;
  }

  return (
    <div className="subsection inline wrap">
      <label className="checkbox">
        <input type="checkbox" checked={boneGraft} onChange={(e) => setBoneGraft(e.target.checked)} /> طعم عظمي
      </label>
      <label className="checkbox">
        <input type="checkbox" checked={membrane} onChange={(e) => setMembrane(e.target.checked)} /> غشاء
      </label>
      <label className="field">
        <span>تاريخ الإجراء</span>
        <input type="date" value={procedureDate} onChange={(e) => setProcedureDate(e.target.value)} />
      </label>
      <label className="field">
        <span>مدة الالتئام (1–6 أشهر)</span>
        <input type="number" min={1} max={6} value={healing} onChange={(e) => setHealing(e.target.value)} />
      </label>
      <button
        type="button"
        onClick={() => void run(() => api.put<VisitDetail>(`/api/doctor/visits/${visitId}/cases/${record.case.id}/implant`, {
          hasBoneGraft: boneGraft, hasMembrane: membrane, procedureDate: procedureDate || null, healingMonths: Number(healing),
        }))}
      >
        حفظ بيانات الزراعة
      </button>
    </div>
  );
}

/** FR-CLN-007: the performing Doctor's own price (entered once per work item; frozen at finalization). */
function Pricing({ record, onChanged, reload }: { record: TreatmentRecord; onChanged: (v: VisitDetail) => void; reload: () => Promise<void> }) {
  const own = record.ownPricing;
  const [price, setPrice] = useState(own?.price ?? '');
  const [error, setError] = useState<unknown>(null);
  const isOrtho = record.case.procedureCode === 'Orthodontics';

  useEffect(() => setPrice(own?.price ?? ''), [own?.price]);

  async function save() {
    setError(null);
    if (!isDecimalText(price)) {
      setError(invalid());
      return;
    }
    try {
      onChanged(await api.put<VisitDetail>(`/api/doctor/treatment-records/${record.recordId}/pricing`, { price: toTwoDecimals(price) }, own?.version));
    } catch (e) {
      setError(e);
    }
  }

  return (
    <div className="subsection">
      {!isOrtho && (
        <div className="inline">
          <label className="field">
            <span>السعر (ج.م)</span>
            <input inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value)} />
          </label>
          <button type="button" className="secondary" onClick={() => void save()}>
            حفظ السعر
          </button>
        </div>
      )}
      {isOrtho && own && <p>إجمالي التقويم: {own.price} ج.م</p>}
      <ErrorBanner error={error} />
      {own && (
        <DiscountEditor url={`/api/doctor/pricing-lines/${own.lineId}/discount`} current={own.discount} entrySeq={own.entrySeq} onSaved={() => void reload()} />
      )}
    </div>
  );
}

/** FR-LAB-001 (Task 3 part): the Doctor's initial Fixed Lab amount, entered once; 0 = explicitly no lab cost. */
function FixedLabEntry({ caseId, editable }: { caseId: string; editable: boolean }) {
  const [lab, setLab] = useState<FixedLab | null>(null);
  const [amount, setAmount] = useState('');
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    api.get<FixedLab>(`/api/doctor/fixed-lab/${caseId}`).then(setLab, setError);
  }, [caseId]);

  async function save() {
    setError(null);
    if (!isDecimalText(amount)) {
      setError(invalid());
      return;
    }
    try {
      setLab(await api.post<FixedLab>(`/api/doctor/fixed-lab/${caseId}`, { amount: toTwoDecimals(amount) }));
    } catch (e) {
      setError(e);
    }
  }

  return (
    <div className="subsection">
      <h4>تكلفة المعمل (نموذج ثابت)</h4>
      {lab?.entered ? (
        <p>تم الإدخال: {lab.currentAmount} ج.م</p>
      ) : editable ? (
        <div className="inline">
          <input inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="0 = لا توجد تكلفة معمل" />
          <button type="button" className="secondary" onClick={() => void save()}>
            حفظ تكلفة المعمل
          </button>
          <span className="hint">مطلوبة قبل إنهاء الزيارة.</span>
        </div>
      ) : (
        <p className="hint">لم تُدخل.</p>
      )}
      <ErrorBanner error={error} />
    </div>
  );
}

function OrthoTerms({ caseId }: { caseId: string }) {
  const [ortho, setOrtho] = useState<OrthoCase | null>(null);
  useEffect(() => {
    api.get<OrthoCase>(`/api/doctor/orthodontic-cases/${caseId}`).then(setOrtho, () => setOrtho(null));
  }, [caseId]);
  if (!ortho) {
    return null;
  }
  return (
    <p>
      {label(ortho.area)}
      {ortho.applianceType ? ` (${ortho.applianceType})` : ''} — الإجمالي: {ortho.terms.grossTotalPrice} ج.م — المقدم: {ortho.terms.depositAmount} ج.م — عدد الأشهر:{' '}
      {ortho.terms.numberOfMonths}
      <span className="hint"> — تُنشأ الأقساط عند إنهاء الزيارة (المرحلة الرابعة).</span>
    </p>
  );
}
