import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import {
  labModelLabels,
  paymentKindLabels,
  paymentMethodLabels,
  roleLabelsAr,
  type LabPercentage,
  type ManagerCharge,
  type ManagerFixedLab,
  type ManagerPayment,
  type PatientCreditBalance,
  type PaymentMethod,
  type PendingEntitlement,
} from '../api/task4';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner, StatusBadge } from '../components/Form';
import { isDecimalText, toTwoDecimals } from '../lib/decimal';
import { formatClinicDateTime } from '../lib/time';
import { useLoad } from '../lib/useLoad';

type Tab = 'charges' | 'payments' | 'credit' | 'pending' | 'settings';

/** FR-REPORT-006 / FR-LAB (Manager only): finance lists, lab percentage, payment methods and Fixed Lab revisions. */
export function FinanceManagerPage() {
  const [tab, setTab] = useState<Tab>('charges');
  return (
    <section>
      <h1>المالية</h1>
      <div className="inline">
        {(
          [
            ['charges', 'المستحقات'],
            ['payments', 'المدفوعات'],
            ['credit', 'الأرصدة الدائنة'],
            ['pending', 'مستحقات أطباء معلقة'],
            ['settings', 'نسبة المعمل ووسائل الدفع'],
          ] as [Tab, string][]
        ).map(([k, label]) => (
          <button key={k} type="button" className={tab === k ? 'picked' : ''} onClick={() => setTab(k)}>
            {label}
          </button>
        ))}
      </div>
      {tab === 'charges' && <Charges />}
      {tab === 'payments' && <Payments />}
      {tab === 'credit' && <Credit />}
      {tab === 'pending' && <Pending />}
      {tab === 'settings' && (
        <>
          <LabPercentageCard />
          <PaymentMethodsCard />
        </>
      )}
    </section>
  );
}

function Charges() {
  const { me } = useAuth();
  const { data, error } = useLoad<ManagerCharge[]>('/api/manager/finance/charges');
  const [fixedLabCase, setFixedLabCase] = useState<string | null>(null);
  return (
    <div className="card">
      <ErrorBanner error={error} />
      {fixedLabCase && <FixedLabCard caseId={fixedLabCase} onClose={() => setFixedLabCase(null)} />}
      <table>
        <thead>
          <tr>
            <th>المريض</th>
            <th>البند</th>
            <th>الطبيب المنفذ</th>
            <th>النسبة</th>
            <th>المعمل</th>
            <th>التاريخ</th>
            <th>المستحق</th>
            <th>المحصل</th>
            <th>المتبقي</th>
            <th>خصم (من)</th>
            <th>للطبيب: مستحق / معترف / معلق</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {data?.map((c) => (
            <tr key={c.id}>
              <td>
                <Link to={`/patients/${c.patient.id}`}>{c.patient.fullName}</Link>
              </td>
              <td>
                {c.kind === 'Consultation' ? 'كشف' : c.procedureName}
                {c.isOrtho && ' (تقويم)'}
              </td>
              <td>{c.performingDoctor?.name ?? '—'}</td>
              <td>{c.doctorPercentageSnapshot ? `${c.doctorPercentageSnapshot}%` : '—'}</td>
              <td>{c.labModel ? labModelLabels[c.labModel] : '—'}</td>
              <td>{formatClinicDateTime(c.chargedAt, me?.clinicTimezone)}</td>
              <td>{c.billed}</td>
              <td>{c.collected}</td>
              <td>{c.outstanding}</td>
              <td>
                {c.discount}
                {c.discountSetByRole && ` (${roleLabelsAr[c.discountSetByRole] ?? c.discountSetByRole})`}
              </td>
              <td>
                {c.doctorEntitled} / {c.doctorRecognized} / {c.pendingEntitlement}
              </td>
              <td>
                {c.labModel === 'Fixed' && c.treatmentCaseId && (
                  <button type="button" onClick={() => setFixedLabCase(c.treatmentCaseId)}>
                    المعمل الثابت
                  </button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** FR-LAB-008..010: the Manager revises an existing Fixed Lab (new audited revision); the initial amount is the Doctor's. */
function FixedLabCard({ caseId, onClose }: { caseId: string; onClose: () => void }) {
  const { data, error, reload } = useLoad<ManagerFixedLab>(`/api/manager/fixed-lab/${caseId}`);
  const [amount, setAmount] = useState('');
  const [reason, setReason] = useState('');
  const [saveError, setSaveError] = useState<unknown>(null);

  async function save() {
    setSaveError(null);
    try {
      await api.put(`/api/manager/fixed-lab/${caseId}`, { amount: isDecimalText(amount) ? toTwoDecimals(amount) : amount, reason });
      setAmount('');
      setReason('');
      await reload();
    } catch (e) {
      setSaveError(e);
    }
  }

  return (
    <div className="subsection">
      <h3>المعمل الثابت — {data?.patient.fullName} — {data?.procedureName}</h3>
      <ErrorBanner error={error ?? saveError} />
      {data && (
        <>
          <p>
            المبلغ الحالي: {data.currentAmount ?? '—'} — المغطى حتى الآن: {data.labCoveredSoFar}
          </p>
          <ul>
            {data.revisions.map((r) => (
              <li key={r.revisionNumber}>
                مراجعة {r.revisionNumber}: {r.amount} — {roleLabelsAr[r.enteredByRole] ?? r.enteredByRole}
                {r.reason ? ` — ${r.reason}` : ''}
              </li>
            ))}
          </ul>
          <input placeholder="المبلغ الجديد" value={amount} onChange={(e) => setAmount(e.target.value)} />
          <input placeholder="السبب" value={reason} onChange={(e) => setReason(e.target.value)} />
          <button type="button" disabled={!amount || !reason.trim()} onClick={() => void save()}>
            مراجعة
          </button>
        </>
      )}
      <button type="button" onClick={onClose}>
        إغلاق
      </button>
    </div>
  );
}

function Payments() {
  const { me } = useAuth();
  const { data, error } = useLoad<ManagerPayment[]>('/api/manager/finance/payments');
  return (
    <div className="card">
      <ErrorBanner error={error} />
      <p className="hint">التصحيحات (إلغاء / استرداد / إعادة توزيع / استبدال) تتم من صفحة المريض.</p>
      <table>
        <thead>
          <tr>
            <th>التاريخ</th>
            <th>المريض</th>
            <th>النوع</th>
            <th>الوسيلة</th>
            <th>المبلغ</th>
            <th>إلى الرصيد الدائن</th>
          </tr>
        </thead>
        <tbody>
          {data?.map((p) => (
            <tr key={p.id}>
              <td>{formatClinicDateTime(p.paidAt, me?.clinicTimezone)}</td>
              <td>
                <Link to={`/patients/${p.patient.id}`}>{p.patient.fullName}</Link>
              </td>
              <td>{paymentKindLabels[p.kind] ?? p.kind}</td>
              <td>{paymentMethodLabels[p.method] ?? p.method}</td>
              <td>{p.amount}</td>
              <td>{p.creditAmount}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Credit() {
  const { data, error } = useLoad<PatientCreditBalance[]>('/api/manager/finance/credit');
  return (
    <div className="card">
      <ErrorBanner error={error} />
      <p className="hint">الرصيد الدائن ليس إيراداً حتى يُستخدم على مستحق.</p>
      <table>
        <tbody>
          {data?.map((c) => (
            <tr key={c.patient.id}>
              <td>
                <Link to={`/patients/${c.patient.id}`}>{c.patient.fullName}</Link>
              </td>
              <td>{c.creditBalance}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Pending() {
  const { data, error } = useLoad<PendingEntitlement[]>('/api/manager/finance/pending-entitlements');
  return (
    <div className="card">
      <ErrorBanner error={error} />
      <p className="hint">مستحقات معلقة بسبب أولوية تغطية المعمل الثابت؛ ليست خصماً ولا ربحاً.</p>
      <table>
        <thead>
          <tr>
            <th>الطبيب</th>
            <th>المريض</th>
            <th>الإجراء</th>
            <th>المستحق</th>
            <th>المعترف به</th>
            <th>معلق</th>
          </tr>
        </thead>
        <tbody>
          {data?.map((p) => (
            <tr key={p.chargeId}>
              <td>{p.doctor.name}</td>
              <td>{p.patient.fullName}</td>
              <td>{p.procedureName}</td>
              <td>{p.entitled}</td>
              <td>{p.recognized}</td>
              <td>{p.pending}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** FR-LAB-001: effective-dated; a change applies to collections from now on, history is never recalculated. */
function LabPercentageCard() {
  const { data, error, reload } = useLoad<LabPercentage>('/api/manager/settings/lab-percentage');
  const [percentage, setPercentage] = useState('');
  const [reason, setReason] = useState('');
  const [saveError, setSaveError] = useState<unknown>(null);

  async function save() {
    setSaveError(null);
    try {
      await api.put('/api/manager/settings/lab-percentage', { percentage: isDecimalText(percentage) ? toTwoDecimals(percentage) : percentage, reason: reason || null });
      setPercentage('');
      setReason('');
      await reload();
    } catch (e) {
      setSaveError(e);
    }
  }

  return (
    <div className="card">
      <h2>نسبة المعمل العام</h2>
      <ErrorBanner error={error ?? saveError} />
      <p>النسبة الحالية: {data?.current ? `${data.current}%` : 'غير مضبوطة'}</p>
      <input placeholder="النسبة الجديدة %" value={percentage} onChange={(e) => setPercentage(e.target.value)} />
      <input placeholder="السبب" value={reason} onChange={(e) => setReason(e.target.value)} />
      <button type="button" disabled={!percentage} onClick={() => void save()}>
        حفظ (يسري على التحصيلات القادمة فقط)
      </button>
      <ul>
        {data?.history.map((h) => (
          <li key={h.id}>
            {h.percentage}% من {h.effectiveFrom.slice(0, 16).replace('T', ' ')}
            {h.reason ? ` — ${h.reason}` : ''}
          </li>
        ))}
      </ul>
    </div>
  );
}

function PaymentMethodsCard() {
  const { data, error, reload } = useLoad<PaymentMethod[]>('/api/manager/payment-methods');
  const [saveError, setSaveError] = useState<unknown>(null);
  return (
    <div className="card">
      <h2>وسائل الدفع</h2>
      <ErrorBanner error={error ?? saveError} />
      <table>
        <tbody>
          {data?.map((m) => (
            <tr key={m.code}>
              <td>{m.displayName}</td>
              <td>
                <StatusBadge active={m.isActive} />
              </td>
              <td>
                <button
                  type="button"
                  onClick={() =>
                    void api
                      .put(`/api/manager/payment-methods/${m.code}`, { isActive: !m.isActive }, m.version)
                      .then(() => reload(), setSaveError)
                  }
                >
                  {m.isActive ? 'إيقاف' : 'تفعيل'}
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
