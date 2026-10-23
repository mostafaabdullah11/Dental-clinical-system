import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import type { PagedResult, PatientListItem } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner, Field } from '../components/Form';
import { formatAge } from '../lib/patients';

/**
 * Patient search (FR-PAT-004). Always a POST body, never a query string. Doctors search by patient code or name
 * only; Sales / Manager may also search by phone. The audience route decides what each result contains.
 */
export function PatientsSearchPage() {
  const { me, can } = useAuth();
  const [query, setQuery] = useState('');
  const [result, setResult] = useState<PagedResult<PatientListItem> | null>(null);
  const [error, setError] = useState<unknown>(null);

  const contactAudience = can('Patients.Search.Contact');
  const url = me?.role === 'MANAGER' ? '/api/manager/patients/search'
    : contactAudience ? '/api/sales/patients/search' : '/api/doctor/patients/search';

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      setResult(await api.post<PagedResult<PatientListItem>>(url, { query, page: 1, pageSize: 25 }));
    } catch (e) {
      setError(e);
    }
  }

  return (
    <section>
      <h1>المرضى</h1>
      {can('Patients.Identity.Write') && can('Patients.Contact.Write') && (
        <p>
          <Link to="/patients/new">تسجيل مريض جديد (مباشر)</Link>
        </p>
      )}
      <form className="card inline" onSubmit={(e) => void submit(e)}>
        <ErrorBanner error={error} />
        <Field label={contactAudience ? 'كود المريض أو الاسم أو رقم الهاتف' : 'كود المريض أو الاسم'} field="query" error={error}>
          <input value={query} onChange={(e) => setQuery(e.target.value)} autoComplete="off" required />
        </Field>
        <button type="submit">بحث</button>
      </form>
      {result && (
        <>
          <p className="hint">عدد النتائج: {result.totalCount}</p>
          <table>
            <thead>
              <tr>
                <th>كود المريض</th>
                <th>الاسم</th>
                <th>العمر</th>
                {contactAudience && <th>الهاتف</th>}
              </tr>
            </thead>
            <tbody>
              {result.items.map((p) => (
                <tr key={p.id}>
                  <td>
                    <Link to={`/patients/${p.id}`}>{p.patientCode}</Link>
                  </td>
                  <td>{p.fullName}</td>
                  <td>{formatAge(p.ageDisplay)}</td>
                  {contactAudience && <td>{p.phone ?? '—'}</td>}
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </section>
  );
}
