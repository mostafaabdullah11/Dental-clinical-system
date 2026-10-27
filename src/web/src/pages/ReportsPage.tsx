import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { FinancialReport, ReportLine } from '../api/task4';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner } from '../components/Form';
import { addDays, clinicToday } from '../lib/time';

type ReportName = 'financial' | 'doctor-performance' | 'sales-performance' | 'orthodontic-receivables' | 'expenses' | 'attendance';

const reportLabels: Record<ReportName, string> = {
  financial: 'التقرير المالي',
  'doctor-performance': 'أداء الأطباء',
  'sales-performance': 'أداء المبيعات',
  'orthodontic-receivables': 'مستحقات التقويم',
  expenses: 'المصروفات',
  attendance: 'الحضور والوقت الإضافي',
};

/** Arabic column labels for the generic table renderer (unknown keys are hidden; ids are never shown). */
const columnLabels: Record<string, string> = {
  doctorName: 'الطبيب', isActive: 'نشط', charges: 'عدد البنود', billed: 'المستحق', collected: 'المحصل', entitlement: 'الاستحقاق',
  recognizedShare: 'الحصة المعترف بها', pendingEntitlement: 'معلق', labAmount: 'المعمل', clinicShare: 'حصة العيادة', name: 'الموظف',
  leadSourcedCollected: 'المحصل من العملاء المحتملين', commissionAccrued: 'العمولة المستحقة', notAccruedInactive: 'غير مستحقة — غير نشط',
  patientCode: 'كود المريض', patientName: 'المريض', effectiveTotal: 'الإجمالي الفعلي', outstanding: 'المتبقي', overdueAmount: 'المتأخر',
  overdueCount: 'أقساط متأخرة', nextDueDate: 'القسط القادم', nextDueAmount: 'قيمة القسط القادم', expenseDate: 'التاريخ', category: 'الفئة',
  description: 'الوصف', amount: 'المبلغ', paymentMethodCode: 'الوسيلة', createdByRole: 'أنشأه', personKind: 'النوع', month: 'الشهر', present: 'حاضر',
  late: 'متأخر', absent: 'غائب', approvedOvertimeHours: 'ساعات إضافية معتمدة', approvedOvertimeAmount: 'قيمة الإضافي',
};

/**
 * FR-REPORT-002..008 (Manager only). Daily / weekly / monthly / custom ranges; a payroll month is included only when the range
 * fully contains it (no proration). CSV export streams from the API; PDF uses the browser's print of this page.
 */
export function ReportsPage() {
  const { me } = useAuth();
  const today = clinicToday(me?.clinicTimezone);
  const [name, setName] = useState<ReportName>('financial');
  const [from, setFrom] = useState(today.slice(0, 8) + '01');
  const [to, setTo] = useState(today);
  const [data, setData] = useState<unknown>(null);
  const [error, setError] = useState<unknown>(null);
  const query = `from=${from}&to=${to}`;

  useEffect(() => {
    setData(null);
    setError(null);
    api.get(`/api/manager/reports/${name}?${query}`).then(setData, setError);
  }, [name, query]);

  function preset(kind: 'day' | 'week' | 'month') {
    if (kind === 'day') {
      setFrom(today);
      setTo(today);
    } else if (kind === 'week') {
      setFrom(addDays(today, -6));
      setTo(today);
    } else {
      setFrom(today.slice(0, 8) + '01');
      setTo(today);
    }
  }

  return (
    <section>
      <h1>التقارير</h1>
      <div className="inline">
        <select value={name} onChange={(e) => setName(e.target.value as ReportName)}>
          {Object.entries(reportLabels).map(([k, label]) => (
            <option key={k} value={k}>
              {label}
            </option>
          ))}
        </select>
        <button type="button" onClick={() => preset('day')}>يومي</button>
        <button type="button" onClick={() => preset('week')}>أسبوعي</button>
        <button type="button" onClick={() => preset('month')}>شهري</button>
        <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        <button type="button" onClick={() => void api.download(`/api/manager/reports/${name}/export?${query}`, `${name}_${from}_${to}.csv`).catch(setError)}>
          تصدير CSV
        </button>
        <button type="button" onClick={() => window.print()}>
          طباعة / PDF
        </button>
      </div>
      <ErrorBanner error={error} />
      {data !== null && (name === 'financial' ? <Financial report={data as FinancialReport} /> : <Generic data={data} />)}
    </section>
  );
}

function Lines({ title, lines }: { title: string; lines: ReportLine[] }) {
  return (
    <>
      <h3>{title}</h3>
      <table>
        <tbody>
          {lines.map((l) => (
            <tr key={l.key}>
              <td>{l.label}</td>
              <td>{l.amount}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}

function Financial({ report: r }: { report: FinancialReport }) {
  return (
    <div className="card">
      <Lines title="الإيرادات" lines={r.revenue} />
      <Lines title="التعويضات" lines={r.compensation} />
      <Lines title="المصروفات" lines={r.expenses} />
      <h3>النتيجة التشغيلية التقديرية للعيادة: {r.estimatedOperatingResult} ج.م</h3>
      <p className="hint">مؤشر تشغيلي وليس ربحاً محاسبياً؛ رواتب الأطباء لا تُخصم مرة أخرى.</p>
      <Lines title="مذكرة" lines={r.memo} />
      <Lines title="المقبوضات حسب الوسيلة" lines={r.cashByMethod} />
      <p>شهور الرواتب المشمولة: {r.includedPayrollMonths.length ? r.includedPayrollMonths.map((m) => m.slice(0, 7)).join('، ') : 'لا يوجد شهر كامل في الفترة'}</p>
      {r.payrollsNotYetFinalized.length > 0 && (
        <p>
          كشوف لم تُعتمد بعد: {r.payrollsNotYetFinalized.map((n) => `${n.personName} (${n.periodStart.slice(0, 7)})`).join('، ')}
        </p>
      )}
    </div>
  );
}

function Generic({ data }: { data: unknown }) {
  const rows = (Array.isArray(data) ? data : ((data as { employees?: unknown[]; rows?: unknown[] }).employees ?? (data as { rows?: unknown[] }).rows ?? [])) as Record<
    string,
    unknown
  >[];
  const extra = !Array.isArray(data) ? (data as Record<string, unknown>) : null;
  const columns = rows.length > 0 ? Object.keys(rows[0]!).filter((k) => columnLabels[k]) : [];
  return (
    <div className="card">
      {extra && 'walkInCollected' in extra && (
        <p>
          المرضى المباشرون (Walk-In): المحصل {String(extra.walkInCollected)} — العمولة {String(extra.walkInCommission)}
        </p>
      )}
      {extra && 'byCategory' in extra && <Lines title="حسب الفئة" lines={extra.byCategory as ReportLine[]} />}
      <table>
        <thead>
          <tr>
            {columns.map((c) => (
              <th key={c}>{columnLabels[c]}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i}>
              {columns.map((c) => (
                <td key={c}>{typeof row[c] === 'boolean' ? (row[c] ? 'نعم' : 'لا') : String(row[c] ?? '—')}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
