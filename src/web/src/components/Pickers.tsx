import { useState } from 'react';
import { api } from '../api/client';
import type { SalesDoctorLookupItem } from '../api/task3';
import type { PagedResult, PatientListItem } from '../api/types';
import { useLoad } from '../lib/useLoad';
import { ErrorBanner } from './Form';

/** Active Doctors for booking (SalesDoctorLookupItem: name and specialty only — never the percentage). */
export function DoctorSelect({ value, onChange, required = true }: { value: string; onChange: (id: string) => void; required?: boolean }) {
  const { data, error } = useLoad<SalesDoctorLookupItem[]>('/api/sales/doctors');
  return (
    <>
      <select value={value} onChange={(e) => onChange(e.target.value)} required={required}>
        <option value="">— اختر الطبيب —</option>
        {data?.map((d) => (
          <option key={d.id} value={d.id}>
            {d.name}
            {d.specialty ? ` — ${d.specialty}` : ''}
          </option>
        ))}
      </select>
      <ErrorBanner error={error} />
    </>
  );
}

export interface PickedPatient {
  id: string;
  patientCode: string;
  fullName: string;
}

/** Patient search (always a POST body). The audience route decides what each result contains. */
export function PatientPicker({ searchUrl, value, onChange }: { searchUrl: string; value: PickedPatient | null; onChange: (p: PickedPatient | null) => void }) {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<PatientListItem[]>([]);
  const [error, setError] = useState<unknown>(null);

  async function search() {
    setError(null);
    try {
      setResults((await api.post<PagedResult<PatientListItem>>(searchUrl, { query, page: 1, pageSize: 10 })).items);
    } catch (e) {
      setError(e);
    }
  }

  if (value) {
    return (
      <div className="picked">
        <span>
          {value.patientCode} — {value.fullName}
        </span>
        <button type="button" className="link" onClick={() => onChange(null)}>
          تغيير
        </button>
      </div>
    );
  }

  return (
    <div className="picker">
      <div className="inline">
        <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="كود المريض أو الاسم أو الهاتف" />
        <button type="button" onClick={() => void search()} disabled={query.trim().length < 2}>
          بحث
        </button>
      </div>
      <ErrorBanner error={error} />
      {results.length > 0 && (
        <ul className="results">
          {results.map((p) => (
            <li key={p.id}>
              <button type="button" className="link" onClick={() => onChange({ id: p.id, patientCode: p.patientCode, fullName: p.fullName })}>
                {p.patientCode} — {p.fullName}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
