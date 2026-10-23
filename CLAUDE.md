# Dental Clinic Management System — Claude Instructions

## 1. Project Status

This repository is the Dental Clinic Management System.

Authoritative business/design baseline:

- Version: 1.1
- Status: OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION

The frozen design documents are stored under:

`/docs`

Do not silently change frozen business behavior.

---

## 2. Source of Truth

Before creating specifications, implementing features, fixing business logic,
or writing tests:

Read ONLY the relevant frozen documents under `/docs`.

Do not read every file in the repository unless necessary.

Use this priority when rules appear to conflict:

1. `/docs/OWNER_REVIEW_NOTES.md`
2. `/docs/DESIGN_BASELINE.md`
3. `/docs/IMPLEMENTATION_GUARDRAILS.md`
4. Relevant specialized design document:
   - `/docs/FINANCE_DESIGN.md`
   - `/docs/SECURITY_DESIGN.md`
   - `/docs/DATABASE_DESIGN.md`
   - `/docs/API_ARCHITECTURE.md`
   - `/docs/DEPLOYMENT_ARCHITECTURE.md`
5. `/docs/OPEN_QUESTIONS.md`
6. `/docs/DESIGN_FREEZE.md`

Do not assume the currently open VS Code file is the complete context.

Inspect relevant repository files before making changes.

---

## 3. Frozen Design Rule

The V1.1 design is frozen.

DO NOT:

- invent business rules
- simplify confirmed flows
- silently change requirements
- silently reinterpret financial rules
- add new roles
- add new statuses
- change privacy boundaries
- reopen answered Owner decisions

If implementation reveals a genuine business conflict:

STOP.

Explain the conflict clearly.

Do not choose new business behavior yourself.

A business change requires a controlled new baseline revision.

Technical decisions that do NOT change business behavior may be made normally.

---

## 4. Application Roles

Application roles are exactly:

- Manager
- Doctor
- Sales / Reception

Assistant is a staff type only.

Assistant has:

- NO account
- NO login
- NO application role
- NO dashboard
- NO patient access
- NO clinical access
- NO finance access

Manager-only Assistant administration is allowed.

Patients have:

- NO account
- NO portal
- NO mobile application

---

## 5. V1 Scope Boundaries

Do NOT implement:

- Inventory management
- X-ray functionality
- Multi-branch
- Patient portal
- Patient mobile app
- Insurance integration
- Digital consent
- Printed invoice
- Printed receipt
- Expense attachments
- Daily Closing
- Assistant login
- Feedback expiry
- Arrived appointment status
- Waiting appointment status

Materials / medical supplies / petty purchases are Expenses.

They are NOT Inventory.

---

## 6. Privacy Rules

Privacy boundaries are mandatory.

### Doctor must NOT receive:

- patient phone
- WhatsApp
- email
- address
- CRM/contact notes
- Original Sales employee

### Sales / Reception must NOT receive:

- Doctor clinical notes
- Doctor referral notes
- Root Canal measurements
- detailed clinical procedure notes
- Prescription medication details
- Doctor percentage
- Doctor Share
- Clinic Share
- protected Lab financial information

Sales / Reception MAY access only the explicitly approved operational data,
including:

- Patient DOB
- Referral Reason
- Referral Destination / Type
- operational Follow-Up Instructions
- structured Patient Medical History intake
- patient-facing price / discount / paid / remaining
- allowed Orthodontic payment/installment information

Use audience-specific DTOs.

Never expose extra data because it is convenient for implementation.

---

## 7. Important Financial Rules

Finance is high risk.

Read `/docs/FINANCE_DESIGN.md` before changing financial code.

Preserve:

- Charge is established at Visit Finalization
- Charge != Payment
- Sales commission uses Collected Money
- Walk-In / Direct patients generate ZERO Sales commission
- Sales commission rates are effective-dated
- rate changes affect future collections only
- historical commission rates never recalculate
- Patient Credit is ledger-based
- overpayments may become Patient Credit
- advance payments may become Patient Credit
- Patient Credit is not treatment revenue until applied
- General Lab uses the rate effective at payment/allocation time
- Fixed Lab uses Lab-First cash priority
- pending Doctor entitlement is supported
- Clinic Share must never become negative for Fixed Lab
- performing Doctor owns their work financially
- patient reassignment does not transfer old Doctor ownership
- inactive Doctor keeps entitlement to old work
- inactive Sales receives no new commission
- financial history is append-only where required
- refunds/corrections use controlled new ledger entries
- finalized payroll is immutable

Never use floating-point types for money.

Use decimal-compatible money types.

---

## 8. Orthodontics

Orthodontics has special financial rules.

Doctor initially enters:

- Gross Total
- Deposit
- Number of Months

At Visit Finalization:

`Effective Ortho Total = Gross Total - Discount`

Then:

`Deposit + Sum(Current Active Installments) = Effective Ortho Total`

Initial installments are generated only when the Visit is finalized and the
Charge is established.

Later Manager changes do NOT automatically regenerate the schedule.

Manager manually adjusts eligible future installments.

Paid history must remain immutable.

A paid/partially paid installment:

- cannot be deleted
- cannot be retired
- cannot be reduced below amount already paid

Gross Total increase:

- creates a positive PriceChange installment

Gross Total decrease:

- NOT supported in V1
- reject with `ortho.total_decrease_not_supported`

Generic PriceCorrection / DiscountCorrection must NOT change an Orthodontic
master total.

---

## 9. Scheduling

Appointments:

- duration is entered manually
- overlapping appointments for the same Doctor are allowed
- exactly seven appointment statuses exist

Do NOT create:

- Arrived status
- Waiting status

Patient arrival is a separate event/timestamp.

Sales / Reception records arrival.

Doctor sees their waiting queue.

Visit start moves Appointment to In Treatment.

Visit Finalization moves Appointment to Completed.

Ending a Visit early may release unused future booked time without deleting
or rewriting Appointment history.

---

## 10. Medical History

Structured Patient Medical History is collected by Sales / Reception.

Doctor has read-only access.

Appointment booking must not depend on Medical History completion.

Do not create duplicate generic medical-history fields.

Do not invent additional clinical detail fields.

---

## 11. Discounts

Discount is fixed EGP only.

Allowed actors:

- Doctor
- Sales / Reception
- Manager

Before Charge establishment:

`Total = Price - Discount`

Sales discount access must not expose Doctor clinical information.

After Charge establishment, use the controlled correction rules defined in
the frozen Finance design.

---

## 12. Attendance and Assistant Overtime

Manager maintains Attendance for:

- Assistants
- Sales / Reception

No biometric/GPS/self-check-in flow.

Attendance alone does NOT automatically deduct salary.

Assistant overtime:

`Overtime Amount = Approved Hours × Hourly Rate Snapshot`

Manager controls:

- overtime hourly rate
- approved overtime hours

Do not automatically infer paid overtime from check-in/check-out times.

Sales has Attendance but no overtime-pay rule.

---

## 13. Expenses

Sales / Reception may create only approved operational expenses such as:

- Materials / Medical Supplies
- Petty / Miscellaneous Expenses

Sales does NOT receive unrestricted expense management or Manager financial
analytics.

Manager retains full Expense management according to the frozen design.

Expenses have no Finalized lifecycle state in V1.

Every relevant edit must be audited.

---

## 14. Feedback

Support both confirmed flows:

### Website Flow

Completed Visit
→ Feedback Request
→ secure token link
→ Patient submits feedback

No token expiry in V1.

### Sales-Entered Flow

Patient tells Sales feedback about a Doctor
→ Sales records it

If related to a specific Visit:

link the existing Feedback Request and mark it Submitted.

Do not create duplicate accepted feedback for one request.

Doctors do not automatically receive feedback results.

---

## 15. Code Change Rules

Before editing:

1. Inspect relevant files.
2. Understand existing implementation.
3. Read relevant frozen design documents.
4. Make the smallest correct change.

Do not rewrite unrelated modules.

Do not perform large refactors unless necessary.

Do not duplicate business logic across multiple modules.

Prefer clear domain/service boundaries.

Keep business rules testable.

---

## 16. Testing Rules

Every implemented flow must include appropriate tests.

For business-critical features, test:

- happy path
- validation failures
- authorization
- privacy boundaries
- important edge cases
- historical immutability where applicable

Finance, Auth, Privacy and Orthodontics require stronger test coverage.

Do not declare a feature complete while relevant tests are failing.

---

## 17. Database Rules

Before declaring database work complete:

- migrations must generate successfully
- migrations must apply to an empty PostgreSQL database
- foreign keys must be valid
- UNIQUE constraints must be valid
- CHECK constraints must be valid
- integration tests must pass

Do not create invalid composite foreign keys.

Referenced FK columns must exist and be backed by PK/UNIQUE requirements as
needed by PostgreSQL.

Never destructively erase required financial/audit history.

---

## 18. Security Rules

Never:

- store plaintext passwords
- make old passwords readable
- expose unauthorized DTO fields
- put sensitive clinical values in logs
- put sensitive clinical values in notifications
- trust frontend authorization alone

Authorization must be enforced server-side.

Manager password reset means setting a NEW password.

---

## 19. Arabic Frontend

V1 application UI is:

- Arabic only
- RTL from the beginning

User-facing:

- labels
- validation messages
- reports
- printable application text

should follow the Arabic/RTL requirement.

Backend/internal code identifiers may remain English.

---

## 20. Mermaid / Documentation

When diagrams are required:

Use Mermaid source only.

Do NOT generate:

- PNG
- SVG
- JPG
- rendered diagrams

Do not modify frozen design documents unless explicitly instructed.

New implementation-phase documentation should be added separately.

---

## 21. Git Rules

Do NOT:

- commit automatically
- push automatically
- create tags automatically
- force push

unless explicitly instructed.

Before a requested commit:

- run relevant tests
- summarize changed files
- summarize test results
- show important remaining issues

The user controls Git commits and pushes.

---

## 22. Token / Context Efficiency

Be context-efficient.

Do NOT read all project files for every task.

Read only:

- files directly involved in the task
- relevant frozen design documents
- dependencies required to understand the change

Do not repeatedly summarize documents unless requested.

Do not regenerate unchanged files.

Prefer targeted searches over reading entire large files.

When starting a new task, first identify the minimum relevant files.

---

## 23. Current Development Process

The project is divided into five major Tasks:

1. FRS + Traceability + Acceptance Criteria
2. Project Foundation + Auth + Users + Patients + CRM
3. Appointments + Doctor Workflow + Clinical + Referrals + Feedback
4. Finance + Labs + Orthodontics + Payroll + Expenses + Attendance
5. Full Integration Testing + Security + Deployment + Client Handover

Complete one Task before moving to the next.

Do not commit/push automatically at Task boundaries.

Wait for explicit instruction.

---

## 24. Current Phase

The frozen Design Baseline v1.1 is complete.

Current phase:

TASK 1

FRS + Traceability + Acceptance Criteria

Do not start Task 2 until Task 1 is explicitly marked complete.

---

## 25. Deferred Items

Only these confirmed deferred items remain:

### Q-ORT-3b

Orthodontic Total Price decrease.

V1 behavior:

Reject with:

`ortho.total_decrease_not_supported`

### OPS-1

Exact physical/storage locations for backup recovery private keys.

Decision required before production go-live.

Neither item blocks implementation.

---

## 26. Final Working Principle

DO NOT BE CREATIVE WITH THE BUSINESS.

BE CREATIVE WITH THE ENGINEERING REQUIRED TO IMPLEMENT THE FROZEN BUSINESS
RULES SAFELY, CLEARLY AND TESTABLY.