import { useEffect, useState, type FormEvent } from 'react';
import { useParams } from 'react-router-dom';

interface PublicForm {
  state: 'Open' | 'feedback.already_submitted';
  doctorName: string | null;
  form: { title: string; ratingLabel: string; ratingMin: number; ratingMax: number; commentLabel: string; commentMaxLength: number; submitLabel: string } | null;
}

/**
 * FR-FDB-005..007: the anonymous, token-bound feedback form (no login, no patient identifier, no expiry). It talks only
 * to the two public routes and never asks the patient for identifying data.
 */
export function PublicFeedbackPage() {
  const { token = '' } = useParams();
  const [form, setForm] = useState<PublicForm | null>(null);
  const [state, setState] = useState<'loading' | 'open' | 'done' | 'already' | 'invalid' | 'error'>('loading');
  const [rating, setRating] = useState(0);
  const [comment, setComment] = useState('');

  useEffect(() => {
    fetch(`/api/public/feedback/${encodeURIComponent(token)}`, { credentials: 'omit', cache: 'no-store' })
      .then(async (response) => {
        if (response.status === 404) {
          setState('invalid');
          return;
        }
        if (!response.ok) {
          setState('error');
          return;
        }
        const body = (await response.json()) as PublicForm;
        setForm(body);
        setState(body.state === 'Open' ? 'open' : 'already');
      })
      .catch(() => setState('error'));
  }, [token]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    const response = await fetch(`/api/public/feedback/${encodeURIComponent(token)}`, {
      method: 'POST',
      credentials: 'omit',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ rating, comment: comment.trim() || null }),
    });
    setState(response.ok ? 'done' : response.status === 409 ? 'already' : response.status === 404 ? 'invalid' : 'error');
  }

  return (
    <main className="public-feedback">
      {state === 'loading' && <p>جارٍ التحميل…</p>}
      {state === 'invalid' && <p>هذا الرابط غير صالح.</p>}
      {state === 'error' && <p>تعذر إرسال الطلب الآن. يرجى المحاولة لاحقاً.</p>}
      {state === 'already' && <p>شكراً لك، تم استلام تقييمك لهذه الزيارة.</p>}
      {state === 'done' && <p>شكراً لك! تم استلام تقييمك.</p>}
      {state === 'open' && form?.form && (
        <form className="card narrow" onSubmit={(e) => void submit(e)}>
          <h1>{form.form.title}</h1>
          {form.doctorName && <p>{form.doctorName}</p>}
          <fieldset className="stars">
            <legend>{form.form.ratingLabel}</legend>
            {Array.from({ length: form.form.ratingMax - form.form.ratingMin + 1 }, (_, i) => form.form!.ratingMin + i).map((n) => (
              <button key={n} type="button" className={n <= rating ? 'star on' : 'star'} aria-label={`${n} من ${form.form!.ratingMax}`} onClick={() => setRating(n)}>
                ★
              </button>
            ))}
          </fieldset>
          <label className="field">
            <span>{form.form.commentLabel}</span>
            <textarea maxLength={form.form.commentMaxLength} value={comment} onChange={(e) => setComment(e.target.value)} />
          </label>
          <button type="submit" disabled={rating === 0}>
            {form.form.submitLabel}
          </button>
        </form>
      )}
    </main>
  );
}
