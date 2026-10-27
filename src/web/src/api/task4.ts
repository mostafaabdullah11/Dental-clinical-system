// Task 4 audience DTOs (finance, orthodontic installments, payroll, expenses, attendance, reports, dashboards) and their
// Arabic labels. Money is always a decimal string from the API; the SPA never computes money with floating point.

export interface PatientRef {
  id: string;
  patientCode: string;
  fullName: string;
}

export interface NamedRef {
  id: string;
  name: string;
}

// ---------- Finance (Sales / Manager patient-facing) ----------

export interface SalesCharge {
  id: string;
  kind: 'Treatment' | 'Consultation';
  procedureName: string;
  treatingDoctor: NamedRef | null;
  visitId: string;
  chargedAt: string;
  originalPrice: string;
  discount: string;
  adjustments: string;
  billed: string;
  collected: string;
  outstanding: string;
  isOrtho: boolean;
  treatmentCaseId: string | null;
}

export interface CreditEntry {
  id: string;
  kind: string;
  amount: string;
  createdAt: string;
}

export interface PatientBalance {
  patient: PatientRef & { phone: string | null };
  billed: string;
  collected: string;
  outstanding: string;
  creditBalance: string;
  charges: SalesCharge[];
  creditEntries: CreditEntry[];
}

export interface AllocationLine {
  allocationId: string;
  chargeId: string;
  procedureName: string;
  amount: string;
  source: 'Payment' | 'Credit';
  reversesAllocationId: string | null;
}

export interface PaymentHistoryItem {
  id: string;
  kind: 'Receipt' | 'Reversal' | 'Refund';
  amount: string;
  method: string;
  paidAt: string;
  allocations: AllocationLine[];
  creditAmount: string;
  reversesPaymentId: string | null;
  correctionId: string | null;
}

export interface PaymentConfirmation {
  paymentId: string;
  amount: string;
  method: string;
  paidAt: string;
  allocations: { allocationId: string; chargeId: string; amount: string }[];
  creditAdded: string;
  resultingOutstanding: string;
  resultingCreditBalance: string;
}

export interface OutstandingItem {
  patient: PatientRef & { phone: string | null };
  outstanding: string;
  openCharges: number;
  oldestChargeAt: string;
}

export interface PaymentMethod {
  code: 'Cash' | 'Visa' | 'InstaPay';
  displayName: string;
  isActive: boolean;
  version: string;
}

export const paymentMethodLabels: Record<string, string> = { Cash: 'نقدي', Visa: 'فيزا', InstaPay: 'إنستاباي' };

export const paymentKindLabels: Record<string, string> = { Receipt: 'تحصيل', Reversal: 'إلغاء', Refund: 'استرداد' };

export const creditKindLabels: Record<string, string> = {
  FromPayment: 'زيادة من دفعة',
  FromReallocation: 'من إعادة توزيع',
  AppliedToCharge: 'مستخدم على مستحق',
  Reversal: 'إلغاء',
  Refund: 'استرداد',
};

// ---------- Finance (Doctor own work) ----------

export interface DoctorOwnWork {
  chargeId: string;
  workItemId: string;
  treatmentCaseId: string;
  visitId: string;
  patient: PatientRef;
  procedureName: string;
  chargedAt: string;
  originalPrice: string;
  discount: string;
  finalPrice: string;
  collected: string;
  remaining: string;
  doctorPercentage: string;
  entitled: string;
  recognized: string;
  pendingEntitlement: string;
  clinicShare: string;
  lab: string;
  labModel: 'General' | 'Fixed';
  labCoveredSoFar: string | null;
  fixedLabAmount: string | null;
}

// ---------- Finance (Manager) ----------

export interface ManagerCharge {
  id: string;
  kind: 'Treatment' | 'Consultation';
  patient: PatientRef;
  procedureName: string;
  performingDoctor: NamedRef | null;
  doctorPercentageSnapshot: string | null;
  labModel: 'General' | 'Fixed' | null;
  visitId: string;
  treatmentCaseId: string | null;
  chargedAt: string;
  originalPrice: string;
  discount: string;
  discountSetByRole: string | null;
  adjustments: string;
  billed: string;
  collected: string;
  outstanding: string;
  isOrtho: boolean;
  doctorEntitled: string;
  doctorRecognized: string;
  pendingEntitlement: string;
}

export interface ManagerPayment {
  id: string;
  patient: PatientRef;
  kind: 'Receipt' | 'Reversal' | 'Refund';
  amount: string;
  method: string;
  paidAt: string;
  receivedByUserId: string;
  reversesPaymentId: string | null;
  correctionId: string | null;
  allocations: AllocationLine[];
  creditAmount: string;
}

export interface PendingEntitlement {
  doctor: NamedRef;
  chargeId: string;
  treatmentCaseId: string;
  patient: PatientRef;
  procedureName: string;
  entitled: string;
  recognized: string;
  pending: string;
}

export interface PatientCreditBalance {
  patient: PatientRef;
  creditBalance: string;
}

export interface LabPercentage {
  current: string | null;
  history: { id: string; percentage: string; effectiveFrom: string; setByUserId: string | null; reason: string | null }[];
}

export interface ManagerFixedLab {
  treatmentCaseId: string;
  patient: PatientRef;
  procedureName: string;
  currentAmount: string | null;
  labCoveredSoFar: string;
  revisions: { revisionNumber: number; amount: string; enteredByRole: string; enteredAt: string; reason: string | null }[];
}

export interface CorrectionResult {
  correctionId: string;
  kind: string;
  originalPaymentId: string;
  newPaymentIds: string[];
  resultingOutstanding: string;
  resultingCreditBalance: string;
}

export const adjustmentKindLabels: Record<string, string> = { DiscountCorrection: 'تصحيح خصم', PriceCorrection: 'تصحيح سعر' };

export const roleLabelsAr: Record<string, string> = { DOCTOR: 'الطبيب', SALES_RECEPTION: 'المبيعات / الاستقبال', MANAGER: 'المدير' };

export const labModelLabels: Record<string, string> = { General: 'معمل عام', Fixed: 'معمل ثابت' };

// ---------- Orthodontics ----------

export type InstallmentStatus = 'Upcoming' | 'Due' | 'PartiallyPaid' | 'Paid' | 'Overdue';

export const installmentStatusLabels: Record<InstallmentStatus, string> = {
  Upcoming: 'قادم',
  Due: 'مستحق اليوم',
  PartiallyPaid: 'مدفوع جزئياً',
  Paid: 'مدفوع',
  Overdue: 'متأخر',
};

export const installmentOriginLabels: Record<string, string> = { Generated: 'مولّد', ManagerAdded: 'أضافه المدير', PriceChange: 'زيادة سعر' };

export interface InstallmentView {
  id: string;
  number: number;
  origin: string;
  dueDate: string;
  amount: string;
  amountAlreadyPaid: string;
  remaining: string;
  status: InstallmentStatus;
  isActive: boolean;
}

export interface OrthoCaseView {
  treatmentCaseId: string;
  area: string;
  applianceType: string | null;
  terms: { revisionNumber: number; grossTotalPrice: string; depositAmount: string; numberOfMonths: number; enteredAt: string };
  chargeEstablished: boolean;
  chargeId: string | null;
  grossTotal: string;
  discount: string;
  effectiveOrthoTotal: string;
  collected: string;
  outstanding: string;
  depositPaid: string;
  unappliedCollections: string;
  scheduleDifference: string;
  monthPlanDifference: number;
  requiresScheduleAdjustment: boolean;
  installments: InstallmentView[];
  history: InstallmentView[];
  allocations: { allocationId: string; allocatedAt: string; amount: string; applied: string; available: string }[];
  version: string;
}

export const orthoAreaLabels: Record<string, string> = { UpperArch: 'الفك العلوي', LowerArch: 'الفك السفلي', Appliance: 'جهاز' };

// ---------- Payroll ----------

export type PayrollStatus = 'Draft' | 'Finalized' | 'Paid';

export const payrollStatusLabels: Record<PayrollStatus, string> = { Draft: 'مسودة', Finalized: 'معتمد', Paid: 'مدفوع' };

export interface PayrollAdjustment {
  kind: string | null;
  amount: string;
  reason: string;
  createdAt: string;
}

export interface DoctorPayroll {
  id: string;
  doctorId: string;
  doctorName: string;
  periodStart: string;
  periodEnd: string;
  revenueTotal: string;
  labTotal: string;
  doctorShareTotal: string;
  clinicShareTotal: string;
  adjustmentsTotal: string;
  finalAmount: string;
  status: PayrollStatus;
  finalizedAt: string | null;
  paidAt: string | null;
  itemCount: number;
  adjustments: PayrollAdjustment[];
}

export interface SalesPayroll {
  id: string;
  salesEmployeeId: string;
  employeeName: string;
  periodStart: string;
  periodEnd: string;
  baseSalarySnapshot: string;
  commissionTotal: string;
  bonusTotal: string;
  deductionsTotal: string;
  netSalary: string;
  status: PayrollStatus;
  finalizedAt: string | null;
  paidAt: string | null;
  commissionItems: { allocationId: string; patientId: string; allocatedAt: string; baseAmount: string; percentage: string; commission: string; basis: string | null }[];
  adjustments: PayrollAdjustment[];
}

export interface AssistantPayroll {
  id: string;
  assistantId: string;
  assistantName: string;
  periodStart: string;
  periodEnd: string;
  salarySnapshot: string;
  basePayAmount: string;
  basePaySource: 'SalarySnapshot' | 'ManagerManual';
  partialPeriodReason: string | null;
  overtimeAmount: string;
  finalAmount: string;
  status: PayrollStatus;
  finalizedAt: string | null;
  paidAt: string | null;
  overtimeItems: { overtimeEntryId: string; approvedHours: string; hourlyRateSnapshot: string; amount: string }[];
}

export const partialPeriodReasonLabels: Record<string, string> = {
  JoinedMidMonth: 'التحق خلال الشهر',
  LeftMidMonth: 'غادر خلال الشهر',
  SalaryChangedInMonth: 'تغير الراتب خلال الشهر',
  OtherPartialMonth: 'شهر جزئي لسبب آخر',
};

// ---------- Attendance and overtime ----------

export type AttendanceStatus = 'Present' | 'Late' | 'Absent';

export const attendanceStatusLabels: Record<AttendanceStatus, string> = { Present: 'حاضر', Late: 'متأخر', Absent: 'غائب' };

export interface AttendanceRecord {
  id: string;
  employeeKind: 'Assistant' | 'Sales';
  employeeId: string;
  employeeName: string;
  date: string;
  status: AttendanceStatus;
  checkInTime: string | null;
  checkOutTime: string | null;
  notes: string | null;
  createdAt: string;
  updatedAt: string | null;
  version: string;
}

export interface OvertimeEntry {
  id: string;
  assistantId: string;
  workDate: string;
  attendanceRecordId: string | null;
  approvedHours: string;
  hourlyRateSnapshot: string;
  amount: string;
  notes: string | null;
  recordedAt: string;
  voidedAt: string | null;
  voidReason: string | null;
}

// ---------- Expenses ----------

export interface ExpenseCategory {
  id: string;
  name: string;
  isActive: boolean;
  salesCanRecord: boolean;
  version: string;
}

export interface Expense {
  id: string;
  expenseDate: string;
  categoryId: string;
  category: string;
  description: string;
  amount: string;
  paymentMethodCode: string | null;
  paidToVendor: string | null;
  notes: string | null;
  createdByUserId: string;
  createdByRole: string;
  createdAt: string;
  updatedByUserId: string | null;
  updatedAt: string | null;
  version: string;
}

// ---------- Reports and dashboards ----------

export interface ReportLine {
  key: string;
  label: string;
  amount: string;
}

export interface AmountByKey {
  key: string;
  label: string;
  amount: string;
}

export interface FinancialReport {
  from: string;
  to: string;
  revenue: ReportLine[];
  compensation: ReportLine[];
  expenses: ReportLine[];
  estimatedOperatingResult: string;
  memo: ReportLine[];
  cashByMethod: AmountByKey[];
  includedPayrollMonths: string[];
  payrollsNotYetFinalized: { kind: string; payrollId: string; personId: string; personName: string; periodStart: string; status: string }[];
}

export interface ManagerDashboard {
  date: string;
  appointmentsByStatus: { key: string; count: number }[];
  arrivalsRecorded: number;
  visitsFinalized: number;
  billedToday: string;
  collectedToday: string;
  cashByMethodToday: AmountByKey[];
  expensesToday: string;
  newLeadsToday: number;
  openFollowUps: number;
  overdueFollowUps: number;
  unassignedLeads: number;
  unassignedFollowUps: number;
  outstandingTotal: string;
  patientCreditTotal: string;
  pendingDoctorEntitlement: string;
  overdueOrthoInstallments: number;
  feedbackRequestsToSend: number;
}

export interface DashboardAppointment {
  id: string;
  startAt: string;
  durationMinutes: number;
  status: string;
  patient: PatientRef;
  doctorName: string;
  arrivedAt: string | null;
}

export interface SalesDashboard {
  today: string;
  todaysAppointments: DashboardAppointment[];
  newLeads: { id: string; name: string; createdAt: string }[];
  pendingFollowUps: { id: string; followUpType: string; dueDate: string; status: string; patient: PatientRef | null; leadName: string | null }[];
  newReferrals: { id: string; patient: PatientRef; referralType: string; createdAt: string }[];
  tomorrowsAppointments: DashboardAppointment[];
  tomorrowsOrthoInstallments: { installmentId: string; caseId: string; patient: PatientRef; installmentNumber: number; dueDate: string; remaining: string }[];
  outstandingBalances: { patient: PatientRef; outstanding: string }[];
  feedbackRequestsToSend: { id: string; patient: PatientRef; doctorName: string; createdAt: string }[];
}

/** Compares two decimal strings without floating point (both normalized to two decimals by the API). */
export function moneyCompare(a: string, b: string): number {
  const norm = (v: string) => {
    const negative = v.startsWith('-');
    const [whole = '0', fraction = '00'] = v.replace('-', '').split('.');
    return { negative, digits: BigInt(whole + (fraction + '00').slice(0, 2)) };
  };
  const x = norm(a);
  const y = norm(b);
  const xv = x.negative ? -x.digits : x.digits;
  const yv = y.negative ? -y.digits : y.digits;
  return xv === yv ? 0 : xv < yv ? -1 : 1;
}

export const isPositive = (value: string) => moneyCompare(value, '0.00') > 0;
