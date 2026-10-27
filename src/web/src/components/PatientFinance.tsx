import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api, newIdempotencyKey } from '../api/client';
import {
  adjustmentKindLabels,
  creditKindLabels,
  isPositive,
  paymentKindLabels,
  paymentMethodLabels,
  type CorrectionResult,
  type OrthoCaseView,
  type PatientBalance,
  type PaymentConfirmation,
  type PaymentHistoryItem,
  type SalesCharge,
} from '../api/task4';
import { useAuth } from '../auth/AuthContext';
import { isDecimalText, toTwoDecimals } from '../lib/decimal';
import { clinicToday, formatClinicDateTime } from '../lib/time';
import { useLoad } from '../lib/useLoad';
import { ErrorBanner, Field } from './Form';
import { SalesEmployeePicker } from './SalesEmployeePicker';

const money = (value: string) => (isDecimalText(value) ? toTwoDecimals(value) : value);

/**
 * Sales / Manager patient finance (FR-FIN-002..009): Billed / Collected / Outstanding / Patient Credit, recording a payment
 * with explicit allocations (any remainder becomes Patient Credit), applying credit, payment history and the patient's
 * orthodontic cases. Patient-facing amounts only: no Doctor percentage, share, entitlement or lab value is ever shown here.
 * The Manager additionally sees the audited correction actions (FR-FIN-012..017).
 */
export function PatientFinance({ patientId }: { patientId: string }) {
  const { can, me } = useAuth();
  const isManager = me?.role === 'MANAGER';
  const balance = useLoad<PatientBalance>(`/api/sales/patients/${patientId}/balance`);
  const history = useLoad<PaymentHistoryItem[]>(`/api/sales/patients/${patientId}/payments`);
  const ortho = useLoad<OrthoCaseView[]>(`/api/sales/orthodontic-cases?patientId=${patientId}`);

  if (!can('Finance.PatientBalance.Read')) {
    return null;
  }

  const reload = async () => {
    await Promise.all([balance.reload(), history.reload(), ortho.reload()]);
  };
  const b = balance.data;

  return (
    <div className="card">
      <h2>الحساب المالي للمريض</h2>
      <ErrorBanner error={balance.error ?? history.error} />
      {b && (
        <>
          <p>
            إجمالي المستحق: <strong>{b.billed}</strong> — المحصل: <strong>{b.collected}</strong> — المتبقي: <strong>{b.outstanding}</strong> — الرصيد الدائن:{' '}
            <strong>{b.creditBalance}</strong> ج.م
          </p>
          <table>
            <thead>
              <tr>
                <th>البند</th>
                <th>الطبيب المعالج</th>
                <th>التاريخ</th>
                <th>السعر</th>
                <th>الخصم</th>
                <th>تعديلات</th>
                <th>المستحق</th>
                <th>المحصل</th>
                <th>المتبقي</th>
                <th>إجراءات</th>
              </tr>
            </thead>
            <tbody>
              {b.charges.map((c) => (
                <ChargeRow key={c.id} charge={c} patientId={patientId} isManager={isManager} onChanged={reload} />
              ))}
            </tbody>
          </table>
          {b.charges.length === 0 && <p className="hint">لا توجد مستحقات بعد. يتم تثبيت المستحق عند إنهاء الطبيب للزيارة.</p>}
          {can('Finance.Payments.Record') && <PaymentForm patientId={patientId} charges={b.charges} onDone={reload} />}
          {can('Finance.PatientCredit.Apply') && isPositive(b.creditBalance) && (
            <ApplyCreditForm patientId={patientId} charges={b.charges} onDone={reload} />
          )}
          {b.creditEntries.length > 0 && (
            <details>
              <summary>حركة الرصيد الدائن</summary>
              <table>
                <tbody>
                  {b.creditEntries.map((e) => (
                    <tr key={e.id}>
                      <td>{formatClinicDateTime(e.createdAt, me?.clinicTimezone)}</td>
                      <td>{creditKindLabels[e.kind] ?? e.kind}</td>
                      <td>{e.amount}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </details>
          )}
        </>
      )}
      {ortho.data && ortho.data.length > 0 && (
        <div className="subsection">
          <h3>حالات التقويم</h3>
          {ortho.data.map((o) => (
            <p key={o.treatmentCaseId}>
              <Link to={`/ortho/${o.treatmentCaseId}?patientId=${patientId}`}>فتح حالة التقويم</Link> — الإجمالي الفعلي: {o.effectiveOrthoTotal} — المحصل: {o.collected} — المتبقي: {o.outstanding}
            </p>
          ))}
        </div>
      )}
      <h3>سجل المدفوعات</h3>
      <table>
        <thead>
          <tr>
            <th>التاريخ</th>
            <th>النوع</th>
            <th>الوسيلة</th>
            <th>المبلغ</th>
            <th>التوزيع</th>
            <th>إلى الرصيد الدائن</th>
            {isManager && can('Finance.Payments.Correct') && <th>تصحيح (المدير)</th>}
          </tr>
        </thead>
        <tbody>
          {history.data?.map((p) => (
            <tr key={p.id}>
              <td>{formatClinicDateTime(p.paidAt, me?.clinicTimezone)}</td>
              <td>{paymentKindLabels[p.kind] ?? p.kind}</td>
              <td>{paymentMethodLabels[p.method] ?? p.method}</td>
              <td>{p.amount}</td>
              <td>
                {p.allocations.map((a) => (
                  <div key={a.allocationId}>
                    {a.procedureName}: {a.amount}
                    {a.source === 'Credit' ? ' (من الرصيد)' : ''}
                  </div>
                ))}
              </td>
              <td>{p.creditAmount}</td>
              {isManager && can('Finance.Payments.Correct') && (
                <td>{p.kind === 'Receipt' && <CorrectionPanel payment={p} charges={b?.charges ?? []} onDone={reload} />}</td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ChargeRow({ charge: c, patientId, isManager, onChanged }: { charge: SalesCharge; patientId: string; isManager: boolean; onChanged: () => Promise<void> }) {
  const { can, me } = useAuth();
  return (
    <tr>
      <td>
        {c.kind === 'Consultation' ? 'كشف' : c.procedureName}
        {c.isOrtho && ' (تقويم)'}
      </td>
      <td>{c.treatingDoctor?.name ?? '—'}</td>
      <td>{formatClinicDateTime(c.chargedAt, me?.clinicTimezone)}</td>
      <td>{c.originalPrice}</td>
      <td>{c.discount}</td>
      <td>{c.adjustments}</td>
      <td>{c.billed}</td>
      <td>{c.collected}</td>
      <td>{c.outstanding}</td>
      <td className="actions">
        {isPositive(c.outstanding) && can('Crm.FollowUps.Manage') && <OutstandingFollowUp patientId={patientId} chargeId={c.id} isManager={isManager} />}
        {isManager && can('Finance.Charges.Adjust') && !c.isOrtho && <AdjustCharge chargeId={c.id} onDone={onChanged} />}
      </td>
    </tr>
  );
}

/** FR-FIN-004: explicit allocations; any remainder becomes Patient Credit automatically. One Idempotency-Key per attempt. */
function PaymentForm({ patientId, charges, onDone }: { patientId: string; charges: SalesCharge[]; onDone: () => Promise<void> }) {
  const open = charges.filter((c) => isPositive(c.outstanding));
  const [amount, setAmount] = useState('');
  const [method, setMethod] = useState('Cash');
  const [allocations, setAllocations] = useState<Record<string, string>>({});
  const [key, setKey] = useState(newIdempotencyKey);
  const [error, setError] = useState<unknown>(null);
  const [result, setResult] = useState<PaymentConfirmation | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      const lines = Object.entries(allocations)
        .filter(([, v]) => v.trim() !== '')
        .map(([chargeId, v]) => ({ chargeId, amount: money(v) }));
      const confirmation = await api.postKeyed<PaymentConfirmation>(
        '/api/sales/payments',
        { patientId, amount: money(amount), method, allocations: lines },
        key,
      );
      setResult(confirmation);
      setAmount('');
      setAllocations({});
      setKey(newIdempotencyKey());
      await onDone();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <form className="subsection" onSubmit={(e) => void submit(e)}>
      <h3>تسجيل دفعة</h3>
      <ErrorBanner error={error} />
      {result && (
        <p className="ok">
          تم تسجيل {result.amount} ج.م — أضيف للرصيد الدائن: {result.creditAdded} — المتبقي: {result.resultingOutstanding} — الرصيد الدائن:{' '}
          {result.resultingCreditBalance}
        </p>
      )}
      <Field label="المبلغ (ج.م)" field="amount" error={error}>
        <input value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="decimal" required />
      </Field>
      <Field label="وسيلة الدفع" field="method" error={error}>
        <select value={method} onChange={(e) => setMethod(e.target.value)}>
          {Object.entries(paymentMethodLabels).map(([code, label]) => (
            <option key={code} value={code}>
              {label}
            </option>
          ))}
        </select>
      </Field>
      {open.map((c) => (
        <Field key={c.id} label={`توزيع على: ${c.kind === 'Consultation' ? 'كشف' : c.procedureName} (المتبقي ${c.outstanding})`} field="allocations" error={error}>
          <input value={allocations[c.id] ?? ''} onChange={(e) => setAllocations({ ...allocations, [c.id]: e.target.value })} inputMode="decimal" />
        </Field>
      ))}
      <p className="hint">أي مبلغ غير موزع يُضاف تلقائياً إلى الرصيد الدائن للمريض.</p>
      <button type="submit">تسجيل الدفعة</button>
    </form>
  );
}

function ApplyCreditForm({ patientId, charges, onDone }: { patientId: string; charges: SalesCharge[]; onDone: () => Promise<void> }) {
  const open = charges.filter((c) => isPositive(c.outstanding));
  const [chargeId, setChargeId] = useState('');
  const [amount, setAmount] = useState('');
  const [key, setKey] = useState(newIdempotencyKey);
  const [error, setError] = useState<unknown>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await api.postKeyed(`/api/sales/patients/${patientId}/credit/apply`, { chargeId: chargeId || null, amount: money(amount) }, key);
      setAmount('');
      setKey(newIdempotencyKey());
      await onDone();
    } catch (e) {
      setError(e);
    }
  }

  if (open.length === 0) {
    return null;
  }

  return (
    <form className="subsection inline" onSubmit={(e) => void submit(e)}>
      <h3>استخدام الرصيد الدائن</h3>
      <ErrorBanner error={error} />
      <Field label="المستحق" field="chargeId" error={error}>
        <select value={chargeId} onChange={(e) => setChargeId(e.target.value)} required>
          <option value="">— اختر —</option>
          {open.map((c) => (
            <option key={c.id} value={c.id}>
              {c.kind === 'Consultation' ? 'كشف' : c.procedureName} — المتبقي {c.outstanding}
            </option>
          ))}
        </select>
      </Field>
      <Field label="المبلغ" field="amount" error={error}>
        <input value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="decimal" required />
      </Field>
      <button type="submit">تطبيق</button>
    </form>
  );
}

/** FR-CRM-005: a Remaining Balance follow-up referencing this charge (never created automatically). */
function OutstandingFollowUp({ patientId, chargeId, isManager }: { patientId: string; chargeId: string; isManager: boolean }) {
  const { me } = useAuth();
  const [open, setOpen] = useState(false);
  const [dueDate, setDueDate] = useState(clinicToday(me?.clinicTimezone));
  const [owner, setOwner] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [done, setDone] = useState(false);

  async function submit() {
    setError(null);
    try {
      await api.post('/api/sales/follow-ups', {
        followUpType: 'RemainingBalance',
        patientId,
        chargeId,
        dueDate,
        salesEmployeeId: isManager ? owner || null : null,
      });
      setDone(true);
      setOpen(false);
    } catch (e) {
      setError(e);
    }
  }

  if (done) {
    return <span className="ok">أُنشئت متابعة</span>;
  }
  if (!open) {
    return (
      <button type="button" onClick={() => setOpen(true)}>
        متابعة المتبقي
      </button>
    );
  }
  return (
    <span>
      <ErrorBanner error={error} />
      <input type="date" value={dueDate} onChange={(e) => setDueDate(e.target.value)} />
      {isManager && <SalesEmployeePicker value={owner} onChange={setOwner} />}
      <button type="button" onClick={() => void submit()}>
        إنشاء
      </button>
    </span>
  );
}

/** Manager only (FR-FIN-011): audited price / discount correction of a non-Orthodontic charge; Billed never below Collected. */
function AdjustCharge({ chargeId, onDone }: { chargeId: string; onDone: () => Promise<void> }) {
  const [open, setOpen] = useState(false);
  const [kind, setKind] = useState('DiscountCorrection');
  const [delta, setDelta] = useState('');
  const [reason, setReason] = useState('');
  const [error, setError] = useState<unknown>(null);

  async function submit() {
    setError(null);
    try {
      const negative = delta.trim().startsWith('-');
      const value = money(delta.trim().replace('-', ''));
      await api.post(`/api/manager/charges/${chargeId}/adjustments`, { kind, amountDelta: negative ? `-${value}` : value, reason });
      setOpen(false);
      setDelta('');
      setReason('');
      await onDone();
    } catch (e) {
      setError(e);
    }
  }

  if (!open) {
    return (
      <button type="button" onClick={() => setOpen(true)}>
        تصحيح المستحق
      </button>
    );
  }
  return (
    <span>
      <ErrorBanner error={error} />
      <select value={kind} onChange={(e) => setKind(e.target.value)}>
        {Object.entries(adjustmentKindLabels).map(([k, label]) => (
          <option key={k} value={k}>
            {label}
          </option>
        ))}
      </select>
      <input placeholder="التغيير (مثال: -200.00)" value={delta} onChange={(e) => setDelta(e.target.value)} />
      <input placeholder="السبب" value={reason} onChange={(e) => setReason(e.target.value)} />
      <button type="button" onClick={() => void submit()}>
        حفظ
      </button>
    </span>
  );
}

/** Manager only (FR-FIN-012..017): reversal, refund, reallocation and replacement are new ledger rows with a reason. */
function CorrectionPanel({ payment, charges, onDone }: { payment: PaymentHistoryItem; charges: SalesCharge[]; onDone: () => Promise<void> }) {
  const [mode, setMode] = useState<'' | 'reversal' | 'refund' | 'reallocate' | 'replace'>('');
  const [reason, setReason] = useState('');
  const [amounts, setAmounts] = useState<Record<string, string>>({});
  const [creditAmount, setCreditAmount] = useState('');
  const [fromAllocationId, setFrom] = useState('');
  const [toChargeId, setTo] = useState('');
  const [amount, setAmount] = useState('');
  const [method, setMethod] = useState(payment.method);
  const [key, setKey] = useState(newIdempotencyKey);
  const [error, setError] = useState<unknown>(null);
  const [result, setResult] = useState<CorrectionResult | null>(null);
  const positive = payment.allocations.filter((a) => !a.reversesAllocationId);
  const url = `/api/manager/payments/${payment.id}`;

  async function submit() {
    setError(null);
    try {
      let body: unknown;
      if (mode === 'reversal') {
        body = { reason };
      } else if (mode === 'refund') {
        body = {
          allocations: Object.entries(amounts)
            .filter(([, v]) => v.trim())
            .map(([allocationId, v]) => ({ allocationId, amount: money(v) })),
          creditAmount: creditAmount.trim() ? money(creditAmount) : '0.00',
          reason,
        };
      } else if (mode === 'reallocate') {
        body = { fromAllocationId: fromAllocationId || null, toChargeId: toChargeId || null, amount: money(amount), reason };
      } else {
        body = {
          amount: money(amount),
          method,
          allocations: Object.entries(amounts)
            .filter(([, v]) => v.trim())
            .map(([chargeId, v]) => ({ chargeId, amount: money(v) })),
          reason,
        };
      }
      setResult(await api.postKeyed<CorrectionResult>(`${url}/${mode}`, body, key));
      setKey(newIdempotencyKey());
      setMode('');
      await onDone();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <div>
      {result && <small className="ok">تم التصحيح — المتبقي: {result.resultingOutstanding} — الرصيد الدائن: {result.resultingCreditBalance}</small>}
      <select value={mode} onChange={(e) => { setMode(e.target.value as typeof mode); setAmounts({}); }}>
        <option value="">— إجراء —</option>
        <option value="reversal">إلغاء الدفعة</option>
        <option value="refund">استرداد</option>
        <option value="reallocate">إعادة توزيع</option>
        <option value="replace">استبدال الدفعة</option>
      </select>
      {mode && (
        <div>
          <ErrorBanner error={error} />
          {mode === 'refund' && (
            <>
              {positive.map((a) => (
                <input key={a.allocationId} placeholder={`من ${a.procedureName}`} value={amounts[a.allocationId] ?? ''}
                  onChange={(e) => setAmounts({ ...amounts, [a.allocationId]: e.target.value })} />
              ))}
              <input placeholder="من الرصيد الدائن" value={creditAmount} onChange={(e) => setCreditAmount(e.target.value)} />
            </>
          )}
          {mode === 'reallocate' && (
            <>
              <select value={fromAllocationId} onChange={(e) => setFrom(e.target.value)}>
                <option value="">من الرصيد الدائن للدفعة</option>
                {positive.map((a) => (
                  <option key={a.allocationId} value={a.allocationId}>
                    من {a.procedureName} ({a.amount})
                  </option>
                ))}
              </select>
              <select value={toChargeId} onChange={(e) => setTo(e.target.value)}>
                <option value="">إلى الرصيد الدائن</option>
                {charges.map((c) => (
                  <option key={c.id} value={c.id}>
                    إلى {c.kind === 'Consultation' ? 'كشف' : c.procedureName}
                  </option>
                ))}
              </select>
              <input placeholder="المبلغ" value={amount} onChange={(e) => setAmount(e.target.value)} />
            </>
          )}
          {mode === 'replace' && (
            <>
              <input placeholder="المبلغ الصحيح" value={amount} onChange={(e) => setAmount(e.target.value)} />
              <select value={method} onChange={(e) => setMethod(e.target.value)}>
                {Object.entries(paymentMethodLabels).map(([code, label]) => (
                  <option key={code} value={code}>
                    {label}
                  </option>
                ))}
              </select>
              {charges.map((c) => (
                <input key={c.id} placeholder={`توزيع على ${c.kind === 'Consultation' ? 'كشف' : c.procedureName}`} value={amounts[c.id] ?? ''}
                  onChange={(e) => setAmounts({ ...amounts, [c.id]: e.target.value })} />
              ))}
            </>
          )}
          <input placeholder="السبب (إلزامي)" value={reason} onChange={(e) => setReason(e.target.value)} />
          <button type="button" className="danger" disabled={!reason.trim()} onClick={() => void submit()}>
            تنفيذ
          </button>
        </div>
      )}
    </div>
  );
}
