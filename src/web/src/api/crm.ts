// CRM audience DTOs (Sales / Manager only; no Doctor screen uses these) and the exact frozen value sets.

export const leadStatuses = ['New', 'Contacted', 'Interested', 'CallLater', 'NoAnswer', 'NotInterested', 'Booked', 'Converted'] as const;
export type LeadStatus = (typeof leadStatuses)[number];

export const leadStatusLabels: Record<LeadStatus, string> = {
  New: 'جديد',
  Contacted: 'تم التواصل',
  Interested: 'مهتم',
  CallLater: 'الاتصال لاحقاً',
  NoAnswer: 'لا يرد',
  NotInterested: 'غير مهتم',
  Booked: 'تم الحجز',
  Converted: 'تم التحويل',
};

export const followUpStatuses = ['New', 'Contacted', 'NoResponse', 'FollowedUp'] as const;
export type FollowUpStatus = (typeof followUpStatuses)[number];

export const followUpStatusLabels: Record<FollowUpStatus, string> = {
  New: 'جديد',
  Contacted: 'تم التواصل',
  NoResponse: 'لم يرد',
  FollowedUp: 'تمت المتابعة',
};

export const followUpTypes = ['Lead', 'DoctorReferral', 'ImplantAppointment', 'OrthoInstallment', 'RemainingBalance', 'GeneralTreatmentFollowUp'] as const;
export type FollowUpType = (typeof followUpTypes)[number];

export const followUpTypeLabels: Record<FollowUpType, string> = {
  Lead: 'عميل محتمل',
  DoctorReferral: 'تحويل من الطبيب',
  ImplantAppointment: 'موعد زراعة',
  OrthoInstallment: 'قسط تقويم',
  RemainingBalance: 'مبلغ متبقٍ',
  GeneralTreatmentFollowUp: 'متابعة علاج عامة',
};

export interface SalesEmployeeRef {
  id: string;
  name: string;
}

export interface Lead {
  id: string;
  name: string;
  phone: string | null;
  source: string | null;
  interestedService: string | null;
  notes: string | null;
  nextFollowUpDate: string | null;
  status: LeadStatus;
  isOpen: boolean;
  convertedPatientId: string | null;
  originalSalesEmployee: SalesEmployeeRef;
  responsibleSalesEmployee: SalesEmployeeRef | null;
  createdAt: string;
  version: string;
}

export interface ContactAttempt {
  id: string;
  attemptedAt: string;
  attemptedByUserId: string;
  outcome: string;
  notes: string | null;
}

export interface FollowUp {
  id: string;
  followUpType: FollowUpType;
  patient: { id: string; patientCode: string; fullName: string } | null;
  leadId: string | null;
  assignedSalesEmployee: SalesEmployeeRef | null;
  dueDate: string;
  status: FollowUpStatus;
  isOpen: boolean;
  notes: string | null;
  result: string | null;
  nextFollowUpDate: string | null;
  createdAt: string;
  version: string;
  attempts: ContactAttempt[] | null;
}

export interface LeadConverted {
  leadId: string;
  patientId: string;
  patientCode: string;
}

export const orNull = (value: string) => (value.trim() ? value.trim() : null);

export const formatDateTime = (value: string) => new Date(value).toLocaleString('ar-EG');
