import type { AgeDisplay, MedicalAnswers } from '../api/types';

/** Arabic rendering of the language-neutral age display (FR-PAT-003). */
export function formatAge(age: AgeDisplay): string {
  if (age.kind === 'Computed') {
    return `${age.years} سنة`;
  }
  return `العمر المسجل عند التسجيل: ${age.years} سنة (سُجّل في ${age.recordedOn ?? '—'})`;
}

/** Exactly the confirmed intake questions (FR-MED-001), in order, with Arabic labels. */
export const medicalQuestions: { key: Exclude<keyof MedicalAnswers, 'lastDentalVisit'>; label: string; femaleOnly?: boolean }[] = [
  { key: 'hasAllergy', label: 'حساسية' },
  { key: 'hasHypertension', label: 'ارتفاع ضغط الدم' },
  { key: 'hasViralHepatitis', label: 'التهاب الكبد الفيروسي' },
  { key: 'hasDiabetes', label: 'السكري' },
  { key: 'hasKidneyProblems', label: 'مشاكل الكلى' },
  { key: 'hasBleedingOrClottingProblem', label: 'مشاكل النزيف / تجلط الدم' },
  { key: 'hasHeartProblems', label: 'مشاكل القلب' },
  { key: 'hasRheumaticDisease', label: 'أمراض روماتيزمية' },
  { key: 'isSmoker', label: 'مدخن' },
  { key: 'hasPreviousSurgicalOperations', label: 'عمليات جراحية سابقة' },
  { key: 'isTakingMedications', label: 'يتناول أدوية حالياً' },
  { key: 'isPregnant', label: 'حامل', femaleOnly: true },
  { key: 'isBreastfeeding', label: 'مرضعة', femaleOnly: true },
];

/** Tri-state: true = نعم, false = لا, null = لم تتم الإجابة / لا ينطبق. Null is never shown or sent as "لا". */
export function triStateLabel(value: boolean | null): string {
  return value === true ? 'نعم' : value === false ? 'لا' : 'لم تتم الإجابة / لا ينطبق';
}

export const emptyAnswers: MedicalAnswers = {
  hasAllergy: null,
  hasHypertension: null,
  hasViralHepatitis: null,
  hasDiabetes: null,
  hasKidneyProblems: null,
  hasBleedingOrClottingProblem: null,
  hasHeartProblems: null,
  hasRheumaticDisease: null,
  isSmoker: null,
  hasPreviousSurgicalOperations: null,
  isTakingMedications: null,
  lastDentalVisit: null,
  isPregnant: null,
  isBreastfeeding: null,
};
