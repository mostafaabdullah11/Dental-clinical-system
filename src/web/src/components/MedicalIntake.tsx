import { useEffect, useState, type FormEvent } from 'react';
import { ApiError, api } from '../api/client';
import type { MedicalAnswers, MedicalIntake, TriState } from '../api/types';
import { emptyAnswers, medicalQuestions, triStateLabel } from '../lib/patients';
import { ErrorBanner, Field } from './Form';

function toAnswers(intake: MedicalIntake): MedicalAnswers {
  const answers = { ...emptyAnswers };
  for (const key of Object.keys(emptyAnswers) as (keyof MedicalAnswers)[]) {
    (answers as Record<string, unknown>)[key] = intake[key];
  }
  return answers;
}

/**
 * Structured Medical History Intake (FR-MED-001/002/005). Sales / Manager collect and edit it; first save creates,
 * later saves replace with If-Match. Every flag is tri-state; "not answered" is sent as null, never as false.
 * The female-only questions are applied by staff when relevant; there is no sex field.
 */
export function MedicalIntakeEditor({ patientId }: { patientId: string }) {
  const url = `/api/sales/patients/${patientId}/medical-history`;
  const [answers, setAnswers] = useState<MedicalAnswers>(emptyAnswers);
  const [etag, setEtag] = useState<string | null>(null);
  const [exists, setExists] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    void (async () => {
      try {
        const result = await api.getVersioned<MedicalIntake>(url);
        setAnswers(toAnswers(result.data));
        setEtag(result.etag);
        setExists(true);
      } catch (e) {
        if (!(e instanceof ApiError && e.status === 404)) {
          setError(e);
        }
      }
    })();
  }, [url]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setSaved(false);
    try {
      const result = await api.putVersioned<MedicalIntake>(url, answers, exists ? etag : null);
      setAnswers(toAnswers(result.data));
      setEtag(result.etag);
      setExists(true);
      setSaved(true);
    } catch (e) {
      setError(e);
    }
  }

  const setFlag = (key: keyof MedicalAnswers, value: TriState) => setAnswers((a) => ({ ...a, [key]: value }));

  return (
    <form className="card narrow" onSubmit={(e) => void submit(e)}>
      <h2>التاريخ الطبي (استبيان منظم)</h2>
      {!exists && <p className="hint">لم يُسجَّل الاستبيان لهذا المريض بعد. الحجز لا يتوقف على تعبئته.</p>}
      <ErrorBanner error={error} />
      {saved && <div className="ok">تم حفظ الاستبيان.</div>}
      {medicalQuestions.map((q) => (
        <label key={q.key} className="field">
          <span>
            {q.label}
            {q.femaleOnly ? ' (للإناث فقط — عند الانطباق)' : ''}
          </span>
          <select
            value={answers[q.key] === null ? '' : String(answers[q.key])}
            onChange={(e) => setFlag(q.key, e.target.value === '' ? null : e.target.value === 'true')}
          >
            <option value="">{triStateLabel(null)}</option>
            <option value="true">{triStateLabel(true)}</option>
            <option value="false">{triStateLabel(false)}</option>
          </select>
        </label>
      ))}
      <Field label="آخر زيارة لطبيب الأسنان (نص حر، 200 حرف كحد أقصى)" field="lastDentalVisit" error={error}>
        <input
          value={answers.lastDentalVisit ?? ''}
          maxLength={200}
          onChange={(e) => setAnswers((a) => ({ ...a, lastDentalVisit: e.target.value ? e.target.value : null }))}
        />
      </Field>
      <button type="submit">حفظ الاستبيان</button>
    </form>
  );
}

/** Read-only intake for Doctors (FR-MED-003). There is no Doctor write. */
export function MedicalIntakeReadOnly({ patientId }: { patientId: string }) {
  const [intake, setIntake] = useState<MedicalIntake | null>(null);
  const [missing, setMissing] = useState(false);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    void (async () => {
      try {
        setIntake(await api.get<MedicalIntake>(`/api/doctor/patients/${patientId}/medical-history`));
      } catch (e) {
        if (e instanceof ApiError && e.status === 404) {
          setMissing(true);
        } else {
          setError(e);
        }
      }
    })();
  }, [patientId]);

  return (
    <section className="card narrow">
      <h2>التاريخ الطبي (للاطلاع فقط)</h2>
      <ErrorBanner error={error} />
      {missing && <p className="hint">لم يُسجَّل استبيان التاريخ الطبي لهذا المريض بعد.</p>}
      {intake && (
        <table>
          <tbody>
            {medicalQuestions.map((q) => (
              <tr key={q.key}>
                <th>{q.label}</th>
                <td>{triStateLabel(intake[q.key])}</td>
              </tr>
            ))}
            <tr>
              <th>آخر زيارة لطبيب الأسنان</th>
              <td>{intake.lastDentalVisit ?? 'لم تتم الإجابة'}</td>
            </tr>
          </tbody>
        </table>
      )}
    </section>
  );
}
