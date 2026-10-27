import { useState } from 'react';
import { api } from '../api/client';
import {
  partialPeriodReasonLabels,
  payrollStatusLabels,
  type AssistantPayroll,
  type DoctorPayroll,
  type SalesPayroll,
} from '../api/task4';
import type { ManagerAssistant } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { DoctorSelect } from '../components/Pickers';
import { ErrorBanner } from '../components/Form';
import { SalesEmployeePicker } from '../components/SalesEmployeePicker';
import { isDecimalText, toTwoDecimals } from '../lib/decimal';
import { clinicToday } from '../lib/time';
import { useLoad } from '../lib/useLoad';

const money = (value: string) => (isDecimalText(value) ? toTwoDecimals(value) : value);
type Kind = 'doctors' | 'sales' | 'assistants';
type AnyPayroll = DoctorPayroll | SalesPayroll | AssistantPayroll;
type Run = (action: () => Promise<unknown>) => Promise<void>;

/**
 * FR-PAYROLL (Manager): calendar-month drafts, finalize only after the month ended (then immutable), mark paid. Doctor =
 * recognized earnings + adjustments; Sales = base salary + commission on collected money + bonus − deductions; Assistant =
 * Base Pay (salary snapshot, or Manager-entered for a partial month with a reason code; never prorated) + approved overtime.
 */
export function PayrollPage() {
  const { me } = useAuth();
  const [kind, setKind] = useState<Kind>('doctors');
  const [month, setMonth] = useState(clinicToday(me?.clinicTimezone).slice(0, 7));
  const [person, setPerson] = useState('');
  const [error, setError] = useState<unknown>(null);
  const list = useLoad<AnyPayroll[]>(`/api/manager/payroll/${kind}?month=${month}`);
  const assistants = useLoad<ManagerAssistant[]>('/api/manager/staff/assistants');

  const run: Run = async (action) => {
    setError(null);
    try {
      await action();
      await list.reload();
    } catch (e) {
      setError(e);
    }
  };

  const draftBody =
    kind === 'doctors' ? { doctorId: person || null, month } : kind === 'sales' ? { salesEmployeeId: person || null, month } : { assistantId: person || null, month };

  return (
    <section>
      <h1>الرواتب</h1>
      <div className="inline">
        <select
          value={kind}
          onChange={(e) => {
            setKind(e.target.value as Kind);
            setPerson('');
          }}
        >
          <option value="doctors">الأطباء</option>
          <option value="sales">المبيعات / الاستقبال</option>
          <option value="assistants">المساعدون</option>
        </select>
        <input type="month" value={month} onChange={(e) => setMonth(e.target.value)} />
      </div>
      <ErrorBanner error={error ?? list.error} />
      <div className="card inline">
        <h2>إنشاء مسودة للشهر</h2>
        {kind === 'doctors' && <DoctorSelect value={person} onChange={setPerson} />}
        {kind === 'sales' && <SalesEmployeePicker value={person} onChange={setPerson} />}
        {kind === 'assistants' && (
          <select value={person} onChange={(e) => setPerson(e.target.value)}>
            <option value="">— اختر المساعد —</option>
            {assistants.data?.map((a) => (
              <option key={a.id} value={a.id}>
                {a.fullName}
              </option>
            ))}
          </select>
        )}
        <button type="button" disabled={!person} onClick={() => void run(() => api.post(`/api/manager/payroll/${kind}/drafts`, draftBody))}>
          إنشاء
        </button>
      </div>
      {list.data?.map((p) => <PayrollCard key={p.id} kind={kind} payroll={p} run={run} />)}
      {list.data?.length === 0 && <p className="hint">لا توجد كشوف لهذا الشهر.</p>}
    </section>
  );
}

function PayrollCard({ kind, payroll, run }: { kind: Kind; payroll: AnyPayroll; run: Run }) {
  const url = `/api/manager/payroll/${kind}/${payroll.id}`;
  const isDraft = payroll.status === 'Draft';

  return (
    <div className="card">
      {kind === 'doctors' && <DoctorSummary p={payroll as DoctorPayroll} />}
      {kind === 'sales' && <SalesSummary p={payroll as SalesPayroll} />}
      {kind === 'assistants' && <AssistantSummary p={payroll as AssistantPayroll} />}
      <p>الحالة: {payrollStatusLabels[payroll.status]}</p>
      {isDraft && kind !== 'assistants' && <AdjustmentForm kind={kind} url={url} run={run} />}
      {isDraft && kind === 'assistants' && <BasePayForm url={url} run={run} />}
      <div className="actions">
        {isDraft && (
          <button type="button" onClick={() => void run(() => api.post(`${url}/finalize`))}>
            اعتماد (بعد نهاية الشهر)
          </button>
        )}
        {payroll.status === 'Finalized' && (
          <button type="button" onClick={() => void run(() => api.post(`${url}/mark-paid`))}>
            تسجيل الدفع
          </button>
        )}
      </div>
    </div>
  );
}

function DoctorSummary({ p }: { p: DoctorPayroll }) {
  return (
    <>
      <h3>
        {p.doctorName} — {p.periodStart.slice(0, 7)}
      </h3>
      <p>
        الإيراد: {p.revenueTotal} — المعمل: {p.labTotal} — حصة الطبيب: {p.doctorShareTotal} — حصة العيادة: {p.clinicShareTotal} — التعديلات: {p.adjustmentsTotal} —
        الصافي: <strong>{p.finalAmount}</strong> ({p.itemCount} بند)
      </p>
    </>
  );
}

function SalesSummary({ p }: { p: SalesPayroll }) {
  return (
    <>
      <h3>
        {p.employeeName} — {p.periodStart.slice(0, 7)}
      </h3>
      <p>
        الأساسي: {p.baseSalarySnapshot} — العمولة: {p.commissionTotal} — المكافآت: {p.bonusTotal} — الخصومات: {p.deductionsTotal} — الصافي:{' '}
        <strong>{p.netSalary}</strong>
      </p>
      <details>
        <summary>بنود العمولة ({p.commissionItems.length})</summary>
        <table>
          <tbody>
            {p.commissionItems.map((i) => (
              <tr key={i.allocationId}>
                <td>{i.allocatedAt.slice(0, 10)}</td>
                <td>{i.baseAmount}</td>
                <td>{i.percentage}%</td>
                <td>{i.commission}</td>
                <td>{i.basis === 'Reversal' ? 'إلغاء' : 'تحصيل'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </>
  );
}

function AssistantSummary({ p }: { p: AssistantPayroll }) {
  return (
    <>
      <h3>
        {p.assistantName} — {p.periodStart.slice(0, 7)}
      </h3>
      <p>
        الراتب: {p.salarySnapshot} — الأجر الأساسي: {p.basePayAmount}
        {p.partialPeriodReason && ` (${partialPeriodReasonLabels[p.partialPeriodReason] ?? p.partialPeriodReason})`} — الوقت الإضافي: {p.overtimeAmount} (
        {p.overtimeItems.length} يوم) — الإجمالي: <strong>{p.finalAmount}</strong>
      </p>
    </>
  );
}

function AdjustmentForm({ kind, url, run }: { kind: Kind; url: string; run: Run }) {
  const [adjKind, setAdjKind] = useState('Bonus');
  const [amount, setAmount] = useState('');
  const [reason, setReason] = useState('');
  const body = () => {
    if (kind === 'sales') {
      return { kind: adjKind, amount: money(amount), reason };
    }
    const negative = amount.trim().startsWith('-');
    const value = money(amount.trim().replace('-', ''));
    return { amount: negative ? `-${value}` : value, reason };
  };
  return (
    <div className="inline">
      {kind === 'sales' && (
        <select value={adjKind} onChange={(e) => setAdjKind(e.target.value)}>
          <option value="Bonus">مكافأة</option>
          <option value="Deduction">خصم</option>
        </select>
      )}
      <input placeholder={kind === 'sales' ? 'المبلغ' : 'المبلغ (سالب للخصم)'} value={amount} onChange={(e) => setAmount(e.target.value)} />
      <input placeholder="السبب" value={reason} onChange={(e) => setReason(e.target.value)} />
      <button type="button" disabled={!amount || !reason.trim()} onClick={() => void run(() => api.post(`${url}/adjustments`, body()))}>
        إضافة تعديل
      </button>
    </div>
  );
}

function BasePayForm({ url, run }: { url: string; run: Run }) {
  const [amount, setAmount] = useState('');
  const [reason, setReason] = useState('JoinedMidMonth');
  return (
    <div className="inline">
      <span className="hint">شهر جزئي: يدخل المدير الأجر الأساسي يدوياً مع السبب (بدون تقسيم آلي).</span>
      <input placeholder="الأجر الأساسي" value={amount} onChange={(e) => setAmount(e.target.value)} />
      <select value={reason} onChange={(e) => setReason(e.target.value)}>
        {Object.entries(partialPeriodReasonLabels).map(([code, label]) => (
          <option key={code} value={code}>
            {label}
          </option>
        ))}
      </select>
      <button type="button" disabled={!amount} onClick={() => void run(() => api.put(`${url}/base-pay`, { amount: money(amount), partialPeriodReason: reason }))}>
        حفظ الأجر الأساسي
      </button>
    </div>
  );
}
