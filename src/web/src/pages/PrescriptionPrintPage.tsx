import { useParams } from 'react-router-dom';
import type { PrescriptionPrint } from '../api/task3';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner } from '../components/Form';
import { formatAge } from '../lib/patients';
import { formatClinicDateTime } from '../lib/time';
import { useLoad } from '../lib/useLoad';

/**
 * FR-RX-003: printable prescription rendered from the print model (clinic name and contact, patient name and age, Doctor,
 * date, medications). The model has no patient contact member, so no phone or address can ever be printed.
 */
export function PrescriptionPrintPage() {
  const { id = '' } = useParams();
  const { me } = useAuth();
  const { data, error } = useLoad<PrescriptionPrint>(`/api/doctor/prescriptions/${id}/print`);

  if (!data) {
    return <ErrorBanner error={error} />;
  }

  return (
    <article className="print-sheet">
      <header>
        <h1>{data.clinicName}</h1>
        <p>{data.clinicContact}</p>
      </header>
      <div className="print-meta">
        <span>المريض: {data.patientName} ({data.patientCode})</span>
        <span>العمر: {formatAge(data.patientAge)}</span>
        <span>
          الطبيب: {data.doctorName}
          {data.doctorSpecialty ? ` — ${data.doctorSpecialty}` : ''}
        </span>
        <span>التاريخ: {formatClinicDateTime(data.date, me?.clinicTimezone)}</span>
      </div>
      <h2>℞</h2>
      <ol className="print-items">
        {data.medications.map((m) => (
          <li key={m.id}>
            <strong>{m.medicationName}</strong>
            <div>{[m.dosage, m.frequency, m.duration].filter(Boolean).join(' — ')}</div>
            {m.instructions && <div>{m.instructions}</div>}
          </li>
        ))}
      </ol>
      {data.generalNotes && <p>{data.generalNotes}</p>}
      <footer>
        <span>توقيع الطبيب: ____________</span>
        <button type="button" className="no-print" onClick={() => window.print()}>
          طباعة
        </button>
      </footer>
    </article>
  );
}
