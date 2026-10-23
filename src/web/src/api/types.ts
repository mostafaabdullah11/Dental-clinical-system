// Audience DTOs of the Manager / auth APIs. Money and percentages are decimal strings ("1000.00"):
// the SPA never does floating-point arithmetic on them.

export type Role = 'MANAGER' | 'DOCTOR' | 'SALES_RECEPTION';

export interface Me {
  displayName: string;
  role: Role;
  permissions: string[];
  clinicTimezone: string | null;
  locale: 'ar';
  direction: 'rtl';
}

export interface ManagerUser {
  id: string;
  userName: string;
  role: Role;
  isActive: boolean;
  staffKind: 'Doctor' | 'SalesEmployee' | null;
  staffId: string | null;
  staffFullName: string | null;
}

export interface ManagerDoctor {
  id: string;
  userId: string;
  userName: string | null;
  fullName: string;
  specialty: string | null;
  currentPercentage: string;
  isActive: boolean;
  deactivatedAt: string | null;
}

export interface ManagerSalesEmployee {
  id: string;
  userId: string;
  userName: string | null;
  fullName: string;
  baseMonthlySalary: string;
  currentCommissionPercentage: string;
  isActive: boolean;
  deactivatedAt: string | null;
}

export interface CommissionRevision {
  revisionNumber: number;
  commissionPercentage: string;
  effectiveFrom: string;
  setByUserId: string | null;
  reason: string | null;
}

export interface ManagerAssistant {
  id: string;
  fullName: string;
  currentSalary: string;
  overtimeHourlyRate: string | null;
  isActive: boolean;
  deactivatedAt: string | null;
}

export interface ManagerSettings {
  clinicTimezone: string;
  doctorDobVisible: boolean;
  clinicName: string;
  clinicContact: string;
}

export const roleLabels: Record<Role, string> = {
  MANAGER: 'المدير',
  DOCTOR: 'طبيب',
  SALES_RECEPTION: 'المبيعات / الاستقبال',
};

export interface AgeDisplay {
  kind: 'Computed' | 'Recorded';
  years: number;
  recordedOn: string | null;
}

export interface DoctorRef {
  id: string;
  name: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface PatientListItem {
  id: string;
  patientCode: string;
  fullName: string;
  ageDisplay: AgeDisplay;
  phone?: string | null;
}

export interface SalesPatient {
  id: string;
  patientCode: string;
  fullName: string;
  dateOfBirth: string | null;
  ageYearsRecorded: number | null;
  ageRecordedOn: string | null;
  ageDisplay: AgeDisplay;
  phone: string | null;
  whatsapp: string | null;
  email: string | null;
  address: string | null;
  crmNotes: string | null;
  patientSource: 'WalkIn' | 'Lead';
  originalSalesEmployee: { id: string; name: string } | null;
  isActive: boolean;
  registeredAt: string;
  currentDoctor: DoctorRef | null;
  /** Null until the Finance module supplies Patient Credit (Task 4). */
  patientCreditBalance: string | null;
  identityVersion: string;
  contactVersion: string;
}

export interface ManagerPatient {
  patient: SalesPatient;
  chiefComplaint: string | null;
}

export interface DoctorPatient {
  id: string;
  patientCode: string;
  fullName: string;
  ageDisplay: AgeDisplay;
  dateOfBirth: string | null;
  chiefComplaint: string | null;
  isActive: boolean;
  registeredAt: string;
  currentDoctor: DoctorRef | null;
}

export type TriState = boolean | null;

export interface MedicalAnswers {
  hasAllergy: TriState;
  hasHypertension: TriState;
  hasViralHepatitis: TriState;
  hasDiabetes: TriState;
  hasKidneyProblems: TriState;
  hasBleedingOrClottingProblem: TriState;
  hasHeartProblems: TriState;
  hasRheumaticDisease: TriState;
  isSmoker: TriState;
  hasPreviousSurgicalOperations: TriState;
  isTakingMedications: TriState;
  lastDentalVisit: string | null;
  isPregnant: TriState;
  isBreastfeeding: TriState;
}

export interface MedicalIntake extends MedicalAnswers {
  patientId: string;
  updatedAt: string;
  etag?: string;
}

export interface PatientAssignment {
  id: string;
  doctor: DoctorRef;
  previousDoctor: DoctorRef | null;
  assignedAt: string;
  endedAt: string | null;
  assignedByUserId: string;
  reason: string;
}

export const sourceLabels: Record<SalesPatient['patientSource'], string> = {
  WalkIn: 'مباشر (Walk-In)',
  Lead: 'عميل محتمل (Lead)',
};
