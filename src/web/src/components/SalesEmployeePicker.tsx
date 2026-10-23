import type { ManagerSalesEmployee } from '../api/types';
import { useLoad } from '../lib/useLoad';

/** Manager only: chooses an ACTIVE Sales employee (owner of a new item, or the target of an assignment). */
export function SalesEmployeePicker({ value, onChange, excludeId }: { value: string; onChange: (id: string) => void; excludeId?: string | null }) {
  const { data } = useLoad<ManagerSalesEmployee[]>('/api/manager/staff/sales-employees');
  const active = (data ?? []).filter((s) => s.isActive && s.id !== excludeId);

  return (
    <select value={value} onChange={(e) => onChange(e.target.value)}>
      <option value="">— اختر موظف المبيعات —</option>
      {active.map((s) => (
        <option key={s.id} value={s.id}>
          {s.fullName}
        </option>
      ))}
    </select>
  );
}
