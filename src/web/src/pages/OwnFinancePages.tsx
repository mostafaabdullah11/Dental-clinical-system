import { labModelLabels, payrollStatusLabels, type DoctorOwnWork, type DoctorPayroll, type OutstandingItem, type SalesPayroll } from '../api/task4';
import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner } from '../components/Form';
import { formatClinicDateTime } from '../lib/time';
import { useLoad } from '../lib/useLoad';

/** FR-FIN-020: the Doctor's own work only — own percentage, entitled / recognized / pending, lab, never patient contact data. */
export function MyWorkPage() {
  const { me } = useAuth();
  const { data, error } = useLoad<DoctorOwnWork[]>('/api/doctor/me/work-items');
  return (
    <section>
      <h1>أعمالي المالية</h1>
      <ErrorBanner error={error} />
      <table>
        <thead>
          <tr>
            <th>المريض</th>
            <th>الإجراء</th>
            <th>التاريخ</th>
            <th>السعر بعد الخصم</th>
            <th>المحصل</th>
            <th>المتبقي</th>
            <th>نسبتي</th>
            <th>المستحق لي</th>
            <th>المعترف به</th>
            <th>معلق</th>
            <th>المعمل</th>
            <th>حصة العيادة</th>
          </tr>
        </thead>
        <tbody>
          {data?.map((w) => (
            <tr key={w.chargeId}>
              <td>
                {w.patient.fullName} — {w.patient.patientCode}
              </td>
              <td>{w.procedureName}</td>
              <td>{formatClinicDateTime(w.chargedAt, me?.clinicTimezone)}</td>
              <td>{w.finalPrice}</td>
              <td>{w.collected}</td>
              <td>{w.remaining}</td>
              <td>{w.doctorPercentage}%</td>
              <td>{w.entitled}</td>
              <td>{w.recognized}</td>
              <td>{w.pendingEntitlement}</td>
              <td>
                {labModelLabels[w.labModel]}: {w.lab}
                {w.labModel === 'Fixed' && ` (مغطى ${w.labCoveredSoFar ?? '0.00'} من ${w.fixedLabAmount ?? '—'})`}
              </td>
              <td>{w.clinicShare}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}

/** Own payroll (Payroll.Doctor.ReadOwn / Payroll.Sales.ReadOwn). */
export function MyPayrollPage() {
  const { me } = useAuth();
  return (
    <section>
      <h1>راتبي</h1>
      {me?.role === 'DOCTOR' ? <DoctorOwnPayroll /> : <SalesOwnPayroll />}
    </section>
  );
}

function DoctorOwnPayroll() {
  const { data, error } = useLoad<DoctorPayroll[]>('/api/doctor/me/payroll');
  return (
    <>
      <ErrorBanner error={error} />
      <table>
        <thead>
          <tr>
            <th>الشهر</th>
            <th>حصتي</th>
            <th>التعديلات</th>
            <th>الصافي</th>
            <th>الحالة</th>
          </tr>
        </thead>
        <tbody>
          {data?.map((p) => (
            <tr key={p.id}>
              <td>{p.periodStart.slice(0, 7)}</td>
              <td>{p.doctorShareTotal}</td>
              <td>{p.adjustmentsTotal}</td>
              <td>{p.finalAmount}</td>
              <td>{payrollStatusLabels[p.status]}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}

function SalesOwnPayroll() {
  const { data, error } = useLoad<SalesPayroll[]>('/api/sales/me/payroll');
  return (
    <>
      <ErrorBanner error={error} />
      <table>
        <thead>
          <tr>
            <th>الشهر</th>
            <th>الراتب الأساسي</th>
            <th>العمولة</th>
            <th>المكافآت</th>
            <th>الخصومات</th>
            <th>الصافي</th>
            <th>الحالة</th>
          </tr>
        </thead>
        <tbody>
          {data?.map((p) => (
            <tr key={p.id}>
              <td>{p.periodStart.slice(0, 7)}</td>
              <td>{p.baseSalarySnapshot}</td>
              <td>{p.commissionTotal}</td>
              <td>{p.bonusTotal}</td>
              <td>{p.deductionsTotal}</td>
              <td>{p.netSalary}</td>
              <td>{payrollStatusLabels[p.status]}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="hint">العمولة على المبالغ المحصلة فعلياً من مرضاك المحولين من عملاء محتملين؛ المرضى المباشرون بدون عمولة.</p>
    </>
  );
}

/** Sales / Manager: patients with Outstanding > 0 (FR-FIN-008). */
export function OutstandingPage() {
  const { me } = useAuth();
  const { data, error } = useLoad<OutstandingItem[]>('/api/sales/outstanding-balances');
  return (
    <section>
      <h1>الأرصدة المتبقية</h1>
      <ErrorBanner error={error} />
      <table>
        <thead>
          <tr>
            <th>المريض</th>
            <th>الهاتف</th>
            <th>المتبقي</th>
            <th>عدد المستحقات</th>
            <th>أقدم مستحق</th>
          </tr>
        </thead>
        <tbody>
          {data?.map((o) => (
            <tr key={o.patient.id}>
              <td>
                <Link to={`/patients/${o.patient.id}`}>{o.patient.fullName}</Link>
              </td>
              <td>{o.patient.phone ?? '—'}</td>
              <td>{o.outstanding}</td>
              <td>{o.openCharges}</td>
              <td>{formatClinicDateTime(o.oldestChargeAt, me?.clinicTimezone)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}
