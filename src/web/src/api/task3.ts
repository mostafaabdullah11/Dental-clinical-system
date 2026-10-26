// Task 3 audience DTOs (appointments, visits, clinical work, prescriptions, referrals, feedback, notifications) and their
// Arabic labels. Money is a decimal string; the SPA never computes with floating point.

import type { AgeDisplay } from './types';

export type AppointmentStatus = 'Scheduled' | 'Confirmed' | 'InTreatment' | 'Completed' | 'Cancelled' | 'NoShow' | 'Rescheduled';
export type AppointmentKind = 'Standard' | 'ImplantFollowUp';

/** Exactly the seven frozen statuses. Arrival is an event (arrivedAt), never a status. */
export const appointmentStatusLabels: Record<AppointmentStatus, string> = {
  Scheduled: 'محجوز',
  Confirmed: 'مؤكد',
  InTreatment: 'قيد العلاج',
  Completed: 'مكتمل',
  Cancelled: 'ملغي',
  NoShow: 'لم يحضر',
  Rescheduled: 'أعيدت جدولته',
};

export const appointmentKindLabels: Record<AppointmentKind, string> = {
  Standard: 'موعد عادي',
  ImplantFollowUp: 'متابعة زراعة',
};

export const isOpenAppointment = (status: AppointmentStatus) => status === 'Scheduled' || status === 'Confirmed';

export interface NamedRef {
  id: string;
  name: string;
}

export interface SalesDoctorLookupItem {
  id: string;
  name: string;
  specialty: string | null;
}

export interface AvailabilityRange {
  id: string;
  date: string;
  start: string;
  end: string;
}

export interface AvailabilityWeek {
  doctorId: string;
  weekStart: string;
  status: 'None' | 'Draft' | 'Published';
  publishedAt: string | null;
  ranges: AvailabilityRange[];
  version: string | null;
}

export interface AvailabilitySlots {
  doctor: NamedRef;
  ranges: AvailabilityRange[];
  booked: { appointmentId: string; startAt: string; effectiveEndAt: string }[];
}

export interface SalesAppointment {
  id: string;
  patient: { id: string; patientCode: string; fullName: string; phone: string | null };
  doctor: NamedRef;
  startAt: string;
  durationMinutes: number;
  effectiveEndAt: string;
  status: AppointmentStatus;
  reason: string;
  salesNotes: string | null;
  kind: AppointmentKind;
  previousAppointmentId: string | null;
  rescheduledToAppointmentId: string | null;
  arrivedAt: string | null;
  arrivalRecordedByUserId: string | null;
  statusChangedAt: string | null;
  statusChangeReason: string | null;
  visitEndedAt: string | null;
  createdAt: string;
  version: string;
}

export interface DoctorPatientRef {
  id: string;
  patientCode: string;
  fullName: string;
  ageDisplay: AgeDisplay;
}

export interface DoctorAppointment {
  id: string;
  patient: DoctorPatientRef;
  startAt: string;
  durationMinutes: number;
  effectiveEndAt: string;
  status: AppointmentStatus;
  reason: string;
  kind: AppointmentKind;
  arrivedAt: string | null;
  canReschedule: boolean;
  version: string;
}

export interface DoctorQueueItem {
  appointmentId: string;
  patient: DoctorPatientRef;
  reason: string;
  startAt: string;
  durationMinutes: number;
  arrivedAt: string;
}

export interface DoctorSchedule {
  weekStart: string;
  published: boolean;
  ranges: AvailabilityRange[];
  appointments: DoctorAppointment[];
}

// ---- Clinical (Doctor audience) ----

export interface OptionField {
  name: string;
  kind: 'enum' | 'text';
  values?: string[];
  required?: boolean;
  requiredWhen?: { field: string; equals: string };
  allowedWhen?: { field: string; equals: string };
}

export interface ProcedureType {
  code: string;
  name: string;
  category: string;
  labModel: 'General' | 'Fixed';
  optionDefinition: { teeth: 'none' | 'optional' | 'single' | 'required'; fields: OptionField[] };
}

export interface Step {
  id: string;
  stepCode: string;
  status: 'Pending' | 'Completed';
  completedAt: string | null;
  completedInVisitId: string | null;
  completedBy: NamedRef | null;
}

export interface Measurement {
  id: string;
  canalNumber: number;
  lengthMm: string;
  recordedInVisitId: string;
  recordedBy: NamedRef;
  recordedAt: string;
}

export interface Implant {
  hasBoneGraft: boolean;
  hasMembrane: boolean;
  procedureDate: string | null;
  healingMonths: number;
}

export interface OwnPricing {
  workItemId: string;
  lineId: string;
  price: string;
  discount: string;
  entrySeq: number;
  version: string;
}

export interface CaseSummary {
  id: string;
  procedureCode: string;
  procedureName: string;
  status: 'Planned' | 'Active' | 'Completed' | 'Cancelled';
  teeth: string[];
  area: string | null;
  details: Record<string, string>;
  labModel: 'General' | 'Fixed';
  startedBy: NamedRef;
  startedAt: string;
  completedAt: string | null;
  version: string;
}

export interface TreatmentRecord {
  recordId: string;
  doctor: NamedRef;
  case: CaseSummary;
  startedInThisVisit: boolean;
  steps: Step[];
  measurements: Measurement[];
  implant: Implant | null;
  ownPricing: OwnPricing | null;
}

export interface VisitDetail {
  id: string;
  patient: DoctorPatientRef;
  doctor: NamedRef;
  appointmentId: string | null;
  status: 'Draft' | 'InProgress' | 'Finalized';
  startedAt: string;
  finalizedAt: string | null;
  clinicalNotes: string | null;
  consultationFee: string | null;
  consultationLine: { lineId: string; fee: string; discount: string; entrySeq: number } | null;
  isOwn: boolean;
  canEdit: boolean;
  treatments: TreatmentRecord[];
  version: string;
}

export interface VisitListItem {
  id: string;
  doctor: NamedRef;
  status: VisitDetail['status'];
  startedAt: string;
  finalizedAt: string | null;
  treatmentCount: number;
}

export const visitStatusLabels: Record<VisitDetail['status'], string> = {
  Draft: 'مسودة',
  InProgress: 'جارية',
  Finalized: 'منتهية',
};

export const caseStatusLabels: Record<CaseSummary['status'], string> = {
  Planned: 'مخططة',
  Active: 'نشطة',
  Completed: 'مكتملة',
  Cancelled: 'ملغاة',
};

export const stepLabels: Record<string, string> = {
  AccessCavity: 'فتح التجويف',
  MechanicalPreparation: 'التحضير الميكانيكي',
  Obturation: 'الحشو النهائي للقنوات',
  PostAndCore: 'الوتد والقلب',
};

/** Arabic labels for catalog option fields and values (the codes stay language-neutral in the API). */
export const optionLabels: Record<string, string> = {
  filling_type: 'نوع الحشو',
  composite_zone: 'منطقة الكومبوزيت',
  extraction_type: 'نوع الخلع',
  material: 'الخامة',
  type: 'النوع',
  quality: 'الجودة',
  name: 'الاسم',
  details: 'التفاصيل',
  area: 'المنطقة',
  appliance_type: 'نوع الجهاز',
  GIC: 'زجاجي أيونومر (GIC)',
  Composite: 'كومبوزيت',
  Anterior: 'أمامي',
  Posterior: 'خلفي',
  Simple: 'بسيط',
  Surgical: 'جراحي',
  AdvancedSurgical: 'جراحي متقدم',
  PorcelainPFM: 'بورسلين / PFM',
  Zirconia: 'زيركونيا',
  Emax: 'إي ماكس',
  Complete: 'كامل',
  Partial: 'جزئي',
  FlexibleAcrylic: 'أكريليك مرن',
  StandardAcrylic: 'أكريليك عادي',
  Basic: 'أساسي',
  HighQuality: 'جودة عالية',
  UpperArch: 'الفك العلوي',
  LowerArch: 'الفك السفلي',
  Appliance: 'جهاز',
};

export const label = (code: string) => optionLabels[code] ?? code;

export interface PricingLineSales {
  lineId: string;
  kind: 'WorkItem' | 'Consultation';
  patient: { id: string; patientCode: string; fullName: string };
  procedureName: string | null;
  treatingDoctor: NamedRef;
  price: string;
  discount: string;
  total: string;
  entrySeq: number;
  lastSetBy: { role: string; at: string } | null;
}

export interface SalesClinicalSummary {
  patientId: string;
  cases: {
    caseId: string;
    procedureName: string;
    treatingDoctor: NamedRef;
    status: CaseSummary['status'];
    price: string | null;
    discount: string | null;
    total: string | null;
    paid: string | null;
    remaining: string | null;
  }[];
  referrals: { referralType: ReferralType; reason: string; followUpInstructions: string | null; status: ReferralStatus; createdAt: string }[];
}

export interface OrthoCase {
  treatmentCaseId: string;
  area: string;
  applianceType: string | null;
  terms: { revisionNumber: number; grossTotalPrice: string; depositAmount: string; numberOfMonths: number; enteredAt: string };
  chargeEstablished: boolean;
}

export interface FixedLab {
  treatmentCaseId: string;
  entered: boolean;
  currentAmount: string | null;
  revisionNumber: number | null;
  enteredAt: string | null;
}

// ---- Prescriptions ----

export interface MedicationItem {
  id: string;
  sortOrder: number;
  medicationName: string;
  dosage: string | null;
  frequency: string | null;
  duration: string | null;
  instructions: string | null;
}

export interface Prescription {
  id: string;
  patientId: string;
  visitId: string;
  doctor: NamedRef;
  generalNotes: string | null;
  createdAt: string;
  visitFinalized: boolean;
  isOwn: boolean;
  items: MedicationItem[];
  version: string;
}

export interface PrescriptionPrint {
  clinicName: string;
  clinicContact: string;
  patientName: string;
  patientCode: string;
  patientAge: AgeDisplay;
  doctorName: string;
  doctorSpecialty: string | null;
  date: string;
  generalNotes: string | null;
  medications: MedicationItem[];
}

// ---- Referrals ----

export type ReferralType = 'Surgery' | 'Implant' | 'Orthodontics' | 'Prosthetics' | 'Other';
export type ReferralStatus = 'New' | 'Contacted' | 'Booked' | 'Completed' | 'Lost';

export const referralTypeLabels: Record<ReferralType, string> = {
  Surgery: 'جراحة',
  Implant: 'زراعة',
  Orthodontics: 'تقويم',
  Prosthetics: 'تركيبات',
  Other: 'أخرى',
};

export const referralStatusLabels: Record<ReferralStatus, string> = {
  New: 'جديد',
  Contacted: 'تم التواصل',
  Booked: 'تم الحجز',
  Completed: 'مكتمل',
  Lost: 'لم يتم',
};

export interface DoctorReferral {
  id: string;
  referralType: ReferralType;
  reason: string;
  notes: string | null;
  followUpInstructions: string | null;
  status: ReferralStatus;
  referringDoctor: NamedRef;
  createdAt: string;
}

/** Sales view: destination, reason and instructions — the Doctor's notes do not exist on this type. */
export interface SalesReferral {
  id: string;
  patient: { id: string; patientCode: string; fullName: string; phone: string | null };
  referralType: ReferralType;
  reason: string;
  followUpInstructions: string | null;
  status: ReferralStatus;
  referringDoctorId: string;
  referringDoctorName: string;
  createdAt: string;
  version: string;
}

// ---- Feedback ----

export type FeedbackRequestState = 'Pending' | 'Sent' | 'Submitted';

export const feedbackStateLabels: Record<FeedbackRequestState, string> = {
  Pending: 'بانتظار الإرسال',
  Sent: 'تم الإرسال',
  Submitted: 'تم التقييم',
};

export interface FeedbackQueueItem {
  id: string;
  patient: { id: string; patientCode: string; fullName: string; phone: string | null; whatsapp: string | null };
  doctor: NamedRef;
  visitId: string;
  status: FeedbackRequestState;
  linkPrepared: boolean;
  linkSentAt: string | null;
  deliveryChannel: string | null;
  createdAt: string;
}

export interface FeedbackResult {
  id: string;
  source: 'PatientWeb' | 'SalesEntered';
  doctor: NamedRef;
  patient: { id: string; patientCode: string; fullName: string };
  visitId: string | null;
  rating: number;
  comment: string | null;
  recordedAt: string;
}

export const feedbackSourceLabels: Record<FeedbackResult['source'], string> = {
  PatientWeb: 'نموذج الموقع',
  SalesEntered: 'سجلته المبيعات',
};

// ---- Notifications ----

export interface InboxItem {
  id: string;
  notificationType: string;
  relatedEntityType: string;
  relatedEntityId: string;
  patient: { id: string; patientCode: string; fullName: string } | null;
  sentAt: string | null;
  readAt: string | null;
}

export const notificationTypeLabels: Record<string, string> = {
  AppointmentTomorrow: 'موعد غداً',
  ImplantFollowUpTomorrow: 'متابعة زراعة غداً',
  OrthodonticInstallmentDueTomorrow: 'قسط تقويم مستحق غداً',
  NewDoctorReferral: 'تحويل جديد من طبيب',
  OutstandingPaymentFollowUp: 'متابعة مبلغ متبقٍ',
  PendingFollowUp: 'متابعة مستحقة',
  FeedbackRequestPending: 'طلب تقييم بانتظار الإرسال',
};

export const discountRoleLabels: Record<string, string> = {
  DOCTOR: 'الطبيب',
  SALES_RECEPTION: 'المبيعات / الاستقبال',
  MANAGER: 'المدير',
};
