import { useState, type FormEvent } from 'react';
import { api } from '../api/client';
import { paymentMethodLabels, roleLabelsAr, type Expense, type ExpenseCategory } from '../api/task4';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner, Field, StatusBadge } from '../components/Form';
import { isDecimalText, toTwoDecimals } from '../lib/decimal';
import { clinicToday } from '../lib/time';
import { useLoad } from '../lib/useLoad';

const money = (value: string) => (isDecimalText(value) ? toTwoDecimals(value) : value);

interface ExpenseForm {
  expenseDate: string;
  categoryId: string;
  description: string;
  amount: string;
  paymentMethodCode: string;
  paidToVendor: string;
  notes: string;
}

const toBody = (f: ExpenseForm) => ({
  expenseDate: f.expenseDate,
  categoryId: f.categoryId || null,
  description: f.description,
  amount: money(f.amount),
  paymentMethodCode: f.paymentMethodCode || null,
  paidToVendor: f.paidToVendor || null,
  notes: f.notes || null,
});

/** FR-EXP-001 / -004 / -005 (Manager): list, create, direct audited edit; categories. No finalization, no attachment, no delete. */
export function ExpensesPage() {
  const { me } = useAuth();
  const today = clinicToday(me?.clinicTimezone);
  const [from, setFrom] = useState(today.slice(0, 8) + '01');
  const [to, setTo] = useState(today);
  const expenses = useLoad<Expense[]>(`/api/manager/expenses?from=${from}&to=${to}`);
  const categories = useLoad<ExpenseCategory[]>('/api/manager/expense-categories');
  const [editing, setEditing] = useState<Expense | null>(null);
  const empty: ExpenseForm = { expenseDate: today, categoryId: '', description: '', amount: '', paymentMethodCode: 'Cash', paidToVendor: '', notes: '' };

  return (
    <section>
      <h1>المصروفات</h1>
      <p className="hint">الخامات والمستلزمات الطبية مصروفات وليست مخزوناً.</p>
      <ErrorBanner error={expenses.error ?? categories.error} />
      <ExpenseEditor
        key={editing?.id ?? 'new'}
        title={editing ? 'تعديل مصروف' : 'إضافة مصروف'}
        initial={editing ? { ...editing, paymentMethodCode: editing.paymentMethodCode ?? '', paidToVendor: editing.paidToVendor ?? '', notes: editing.notes ?? '' } : empty}
        categories={(categories.data ?? []).filter((c) => c.isActive)}
        onSave={async (form) => {
          if (editing) {
            await api.put(`/api/manager/expenses/${editing.id}`, toBody(form), editing.version);
          } else {
            await api.post('/api/manager/expenses', toBody(form));
          }
          setEditing(null);
          await expenses.reload();
        }}
        onCancel={editing ? () => setEditing(null) : undefined}
      />
      <div className="inline">
        <label className="field">
          <span>من</span>
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label className="field">
          <span>إلى</span>
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
      </div>
      <table>
        <thead>
          <tr>
            <th>التاريخ</th>
            <th>الفئة</th>
            <th>الوصف</th>
            <th>المبلغ</th>
            <th>الوسيلة</th>
            <th>أنشأه</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {expenses.data?.map((e) => (
            <tr key={e.id}>
              <td>{e.expenseDate}</td>
              <td>{e.category}</td>
              <td>{e.description}</td>
              <td>{e.amount}</td>
              <td>{e.paymentMethodCode ? paymentMethodLabels[e.paymentMethodCode] : '—'}</td>
              <td>{roleLabelsAr[e.createdByRole] ?? e.createdByRole}</td>
              <td>
                <button type="button" onClick={() => setEditing(e)}>
                  تعديل
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <CategoriesCard categories={categories.data ?? []} onChanged={() => void categories.reload()} />
    </section>
  );
}

function ExpenseEditor({ title, initial, categories, onSave, onCancel }: {
  title: string;
  initial: ExpenseForm;
  categories: ExpenseCategory[];
  onSave: (form: ExpenseForm) => Promise<void>;
  onCancel?: () => void;
}) {
  const [form, setForm] = useState(initial);
  const [error, setError] = useState<unknown>(null);
  const set = (k: keyof ExpenseForm) => (e: { target: { value: string } }) => setForm({ ...form, [k]: e.target.value });

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await onSave(form);
      setForm(initial);
    } catch (e) {
      setError(e);
    }
  }

  return (
    <form className="card inline" onSubmit={(e) => void submit(e)}>
      <h2>{title}</h2>
      <ErrorBanner error={error} />
      <Field label="التاريخ" field="expenseDate" error={error}>
        <input type="date" value={form.expenseDate} onChange={set('expenseDate')} required />
      </Field>
      <Field label="الفئة" field="categoryId" error={error}>
        <select value={form.categoryId} onChange={set('categoryId')} required>
          <option value="">— اختر —</option>
          {categories.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>
      </Field>
      <Field label="الوصف" field="description" error={error}>
        <input value={form.description} onChange={set('description')} required />
      </Field>
      <Field label="المبلغ (ج.م)" field="amount" error={error}>
        <input value={form.amount} onChange={set('amount')} inputMode="decimal" required />
      </Field>
      <Field label="وسيلة الدفع" field="paymentMethodCode" error={error}>
        <select value={form.paymentMethodCode} onChange={set('paymentMethodCode')}>
          <option value="">—</option>
          {Object.entries(paymentMethodLabels).map(([code, label]) => (
            <option key={code} value={code}>
              {label}
            </option>
          ))}
        </select>
      </Field>
      <Field label="المورد / المستفيد" field="paidToVendor" error={error}>
        <input value={form.paidToVendor} onChange={set('paidToVendor')} />
      </Field>
      <Field label="ملاحظات" field="notes" error={error}>
        <input value={form.notes} onChange={set('notes')} />
      </Field>
      <button type="submit">حفظ</button>
      {onCancel && (
        <button type="button" onClick={onCancel}>
          إلغاء
        </button>
      )}
    </form>
  );
}

function CategoriesCard({ categories, onChanged }: { categories: ExpenseCategory[]; onChanged: () => void }) {
  const [error, setError] = useState<unknown>(null);
  async function update(c: ExpenseCategory, patch: Partial<ExpenseCategory>) {
    setError(null);
    try {
      const next = { ...c, ...patch };
      await api.put(`/api/manager/expense-categories/${c.id}`, { name: next.name, isActive: next.isActive, salesCanRecord: next.salesCanRecord }, c.version);
      onChanged();
    } catch (e) {
      setError(e);
    }
  }
  return (
    <div className="card">
      <h2>فئات المصروفات</h2>
      <ErrorBanner error={error} />
      <table>
        <thead>
          <tr>
            <th>الفئة</th>
            <th>الحالة</th>
            <th>يسجلها موظف المبيعات</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {categories.map((c) => (
            <tr key={c.id}>
              <td>{c.name}</td>
              <td>
                <StatusBadge active={c.isActive} />
              </td>
              <td>{c.salesCanRecord ? 'نعم' : 'لا'}</td>
              <td>
                <button type="button" onClick={() => void update(c, { isActive: !c.isActive })}>
                  {c.isActive ? 'إيقاف' : 'تفعيل'}
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** FR-EXP-002 / -003 (Sales): create only, in the Sales-recordable categories; no list, no totals, no edit. */
export function SalesExpensePage() {
  const categories = useLoad<{ id: string; name: string }[]>('/api/sales/expenses/allowed-categories');
  const [categoryId, setCategoryId] = useState('');
  const [amount, setAmount] = useState('');
  const [specification, setSpecification] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [created, setCreated] = useState<{ category: string; amount: string; expenseDate: string } | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      setCreated(await api.post('/api/sales/expenses', { categoryId: categoryId || null, amount: money(amount), specification }));
      setAmount('');
      setSpecification('');
    } catch (e) {
      setError(e);
    }
  }

  return (
    <section>
      <h1>تسجيل مصروف تشغيلي</h1>
      <ErrorBanner error={categories.error} />
      <form className="card narrow" onSubmit={(e) => void submit(e)}>
        <ErrorBanner error={error} />
        {created && (
          <p className="ok">
            تم تسجيل {created.amount} ج.م — {created.category} — {created.expenseDate}
          </p>
        )}
        <Field label="الفئة" field="categoryId" error={error}>
          <select value={categoryId} onChange={(e) => setCategoryId(e.target.value)} required>
            <option value="">— اختر —</option>
            {categories.data?.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </Field>
        <Field label="المبلغ (ج.م)" field="amount" error={error}>
          <input value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="decimal" required />
        </Field>
        <Field label="البيان" field="specification" error={error}>
          <input value={specification} onChange={(e) => setSpecification(e.target.value)} required />
        </Field>
        <button type="submit">تسجيل</button>
      </form>
    </section>
  );
}
