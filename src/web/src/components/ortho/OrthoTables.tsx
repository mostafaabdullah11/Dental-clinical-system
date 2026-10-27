import { installmentOriginLabels, installmentStatusLabels, type InstallmentView, type OrthoCaseView } from '../../api/task4';
import { useAuth } from '../../auth/AuthContext';
import { formatClinicDateTime } from '../../lib/time';

/** FR-ORT-007: the status is derived by the server (Upcoming / Due / Partially Paid / Paid / Overdue). */
export function InstallmentTable({ installments }: { installments: InstallmentView[] }) {
  return (
    <table>
      <thead>
        <tr>
          <th>رقم القسط</th>
          <th>النوع</th>
          <th>تاريخ الاستحقاق</th>
          <th>المبلغ المستحق</th>
          <th>المدفوع</th>
          <th>المتبقي</th>
          <th>الحالة</th>
        </tr>
      </thead>
      <tbody>
        {installments.map((i) => (
          <tr key={i.id}>
            <td>{i.number}</td>
            <td>{installmentOriginLabels[i.origin] ?? i.origin}</td>
            <td>{i.dueDate}</td>
            <td>{i.amount}</td>
            <td>{i.amountAlreadyPaid}</td>
            <td>{i.remaining}</td>
            <td>{i.isActive ? installmentStatusLabels[i.status] : 'ملغي'}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/** Collections on the ortho charge and how much of each is already applied to installments (Sales / Manager contract only). */
export function OrthoCollections({ allocations }: { allocations: OrthoCaseView['allocations'] }) {
  const { me } = useAuth();
  return (
    <div className="card">
      <h2>سجل المدفوعات على التقويم</h2>
      {allocations.length === 0 ? (
        <p className="hint">لا توجد مدفوعات بعد. تُسجَّل الدفعة من الحساب المالي للمريض ثم تُطبَّق على الأقساط هنا.</p>
      ) : (
        <table>
          <thead>
            <tr>
              <th>التاريخ</th>
              <th>المبلغ</th>
              <th>المطبق على الأقساط</th>
              <th>المتاح للتطبيق</th>
            </tr>
          </thead>
          <tbody>
            {allocations.map((a) => (
              <tr key={a.allocationId}>
                <td>{formatClinicDateTime(a.allocatedAt, me?.clinicTimezone)}</td>
                <td>{a.amount}</td>
                <td>{a.applied}</td>
                <td>{a.available}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
