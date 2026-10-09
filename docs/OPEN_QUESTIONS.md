# OPEN_QUESTIONS.md

**Design Baseline Version:** 1.1
**Status:** PROPOSED FREEZE — AWAITING OWNER APPROVAL

This is the single register of every decision the user has **not** made. None of these is answered, defaulted into a business rule, or silently implemented. Where the system must behave in some way meanwhile, the **CURRENT SYSTEM BEHAVIOR** field states exactly what that is, and it is always either "feature not built / no route mapped" or a purely technical seam that works under any future answer.

Sources: Step 4 (original list), the later Step 9 reclassifications (Q-FIN-6 redefined, W2 reopened, Q-FIN-9 pending attribution), Step 11 (Q-ORT-2 not resolved via the API, appointment-status actor, walk-in Original Sales Employee), and the operational items from Steps 8, 12, 13, 14.

**How the three groups were assigned.** The design as frozen contains no question that prevents starting implementation (every question is isolated behind a seam). "Blocks implementation now" therefore means: the answer is needed before the named module or screen can be **completed correctly** in the first implementation phases, or building it would force a guess. "Blocks a later module" means the gated work is scheduled later and the seam holds until then. "Non-blocking before V1 go-live" means no code waits for it, but go-live cannot happen without it. This is a sequencing judgement, not a business decision; reorder freely.

Fields per item: **ID · AREA · QUESTION · WHY IT MATTERS · WHAT IT BLOCKS · CURRENT SYSTEM BEHAVIOR · WHEN IT MUST BE ANSWERED**.

---

# GROUP 1: BLOCKS IMPLEMENTATION NOW

### Q-UI-1
- **AREA:** Frontend / localisation
- **QUESTION:** Which UI language(s): Arabic, English, or bilingual?
- **WHY IT MATTERS:** Arabic requires right-to-left layout and an i18n structure from the first screen; retrofitting RTL and string extraction is expensive and touches every component, print template and PDF.
- **WHAT IT BLOCKS:** Frontend scaffolding (layout direction, i18n setup, fonts), prescription print layout, report/PDF templates.
- **CURRENT SYSTEM BEHAVIOR:** API is language-neutral (codes, not messages; the SPA branches on `code`). No UI language is chosen; nothing in the backend depends on it.
- **WHEN IT MUST BE ANSWERED:** Before the first frontend screen is built.

### Q-APP-1
- **AREA:** Scheduling
- **QUESTION:** What is the standard appointment duration? Does it vary by procedure? May appointments overlap?
- **WHY IT MATTERS:** The confirmed rule "accidental double-booking is prevented" needs a definition of when two appointments conflict, and slot generation from availability ranges needs a slot length.
- **WHAT IT BLOCKS:** Overlap-based conflict detection, slot generation from availability ranges, `default_appointment_minutes` setting, duration-aware calendar rendering.
- **CURRENT SYSTEM BEHAVIOR:** `duration_minutes` is nullable. Double-booking is prevented only for the **same doctor and exact same start time** (partial unique index). `default_appointment_minutes` is unset. An overlap exclusion constraint can replace the unique index additively once defined.
- **WHEN IT MUST BE ANSWERED:** Before the Scheduling module's booking and slot features are completed.

### Q-CRM-1
- **AREA:** CRM / follow-ups
- **QUESTION:** What is the list of follow-up statuses, and which of them mean the follow-up is "open"?
- **WHY IT MATTERS:** The user confirmed a follow-up "Status" field but never listed its values. The follow-up planner (J3), the Sales dashboard "pending follow-ups" count and the inactive-owner metric all depend on an "open" predicate.
- **WHAT IT BLOCKS:** Completing job J3 (follow-up due notifications), the pending-follow-ups dashboard figure, and validation of follow-up status transitions.
- **CURRENT SYSTEM BEHAVIOR:** Follow-up `status` is stored as text validated against a list in code (technical placeholder). Crm owns an `IsOpen` predicate that is not bound to a confirmed list.
- **WHEN IT MUST BE ANSWERED:** Before the Crm module's follow-up workflow and J3 are completed.

### Q-FIN-W1 (wiring point W1)
- **AREA:** Finance / Clinical integration
- **QUESTION:** When is a treatment charge established: (A) when the Doctor finalizes the visit containing the priced work item, (B) when Sales/Manager confirms the agreement, or (C) another moment? Also: who creates a consultation charge and where does its amount come from?
- **WHY IT MATTERS:** It decides when "Billed" first appears and whether a step the user has not described (option B) must be built. Consultation fees cannot be recorded at all until the source of a consultation charge is known.
- **WHAT IT BLOCKS:** Wiring `Finance.EstablishCharge` into the visit flow; consultation fee recording; therefore Billed/Outstanding for new work.
- **CURRENT SYSTEM BEHAVIOR:** `EstablishCharge` exists as an internal command with a single call site; nothing calls it. No charge-establishment or consultation route is mapped. Doctor price entry is held on `case_work_items.entered_price`.
- **WHEN IT MUST BE ANSWERED:** Before the Clinical–Finance integration is implemented (before any charge can exist).

### Q-ACC-1
- **AREA:** Identity / account recovery
- **QUESTION:** How are password-reset links or codes delivered to staff (staff email, mobile, or another clinic-defined process)? Do staff have an email or mobile identity in the system?
- **WHY IT MATTERS:** Reset tokens are generated, but without a delivery route a locked-out staff member (and every account after a restore lockdown) cannot recover. "Manager delivers the link" was withdrawn.
- **WHAT IT BLOCKS:** An end-user password-reset initiation endpoint; the new-account communication process; the usability of `post-restore-lockdown`.
- **CURRENT SYSTEM BEHAVIOR:** Delivery is a deployment/configuration detail through the channel abstraction; no staff email/mobile field exists; **no recovery-initiation endpoint is exposed**. Reset-token issuance is available only as an operator command. New accounts get a creator-supplied initial password with must-change-on-first-login.
- **WHEN IT MUST BE ANSWERED:** Before the Identity module's password-reset flow is completed (and in any case before go-live).

---

# GROUP 2: BLOCKS A LATER MODULE

## Finance

### Q-FIN-1
- **AREA:** Finance / fixed lab
- **QUESTION:** When a case's single fixed lab expense (Implant, Fixed Prosthetics, Removable Prosthetics) is paid for through several partial payments, charges and Doctors, how much of each payment is the Lab portion?
- **WHY IT MATTERS:** It determines the Lab and Clinic Share attributed to each fixed-lab collection. A negative Clinic Share early in a case depends on it. It never affects Doctor Share.
- **WHAT IT BLOCKS:** Lab and Clinic completion rows for fixed-lab allocations only; therefore the Lab and Clinic Share figures for implants and prostheses and the Clinic Gross Revenue contribution of those collections.
- **CURRENT SYSTEM BEHAVIOR:** When attribution is clear, a Doctor earning is recorded immediately at the work's percentage snapshot (so Doctor Share and Doctor payroll are **not** blocked). No completion row is created; the remaining amount is shown as **Pending completion**. One fixed lab per case with full revision history is stored. A later versioned calculator backfills with no schema change. No validation exists for negative Clinic Share.
- **WHEN IT MUST BE ANSWERED:** Before fixed-lab Lab/Clinic reporting is finalized (before Reporting is completed); not needed to build payments or payroll.

### Q-FIN-W2 (wiring point W2)
- **AREA:** Finance / general lab
- **QUESTION:** Which general lab percentage applies to a payment: the rate in force when the case/charge began, or the rate at payment time (or another rule)?
- **WHY IT MATTERS:** When the Manager changes the general lab percentage, payments on cases straddling the change are ambiguous.
- **WHAT IT BLOCKS:** Lab/Clinic completion for general-lab cases that straddle a rate change.
- **CURRENT SYSTEM BEHAVIOR:** Nothing is defaulted. A completion is generated only when every candidate rule gives the same answer (rate at the charge's `charged_at` equals the rate at the payment's `paid_at`); otherwise the completion stays pending and shows under Pending completion. The rate used is recorded on every row. Doctor Shares are never affected. Whether "case start" means the case's own start time is part of this answer.
- **WHEN IT MUST BE ANSWERED:** Before the first change to the general lab percentage is made in production (and before Reporting is completed).

### Q-FIN-2
- **AREA:** Sales payroll / commission
- **QUESTION:** What is the mathematical base the Sales commission percentage applies to (collected money, treatment value, clinic share, or other)? May a Sales payroll be finalized before this is configured?
- **WHY IT MATTERS:** Without a base no commission amount can be computed; the Estimated Operating Result also lacks Sales compensation.
- **WHAT IT BLOCKS:** Commission amounts, Sales payroll finalization, completeness of the Estimated Operating Result.
- **CURRENT SYSTEM BEHAVIOR:** Commission percentage per employee and Original Sales Employee attribution are stored. `ISalesCommissionBase` has no implementation; the system never guesses a commission. Base salary, bonus and deductions work. The Sales payroll **finalize action is disabled behind a configuration flag**. Reports show a "Sales commission not configured" notice.
- **WHEN IT MUST BE ANSWERED:** Before the Payroll module's Sales payroll is completed.

### Q-FIN-3
- **AREA:** Finance / discounts
- **QUESTION:** Which role initially enters or approves the agreed discount (fixed EGP amount)?
- **WHY IT MATTERS:** The Doctor does not independently apply discounts; someone must be able to, or no discount can exist.
- **WHAT IT BLOCKS:** Any non-zero discount.
- **CURRENT SYSTEM BEHAVIOR:** `Finance.Discount.Set` exists as an internal key assigned to **no role**; **no route is mapped**. Every charge has `discount_amount = 0` and `discount_set_by_user_id = NULL`. Assigning the role later needs no schema change.
- **WHEN IT MUST BE ANSWERED:** Before go-live if the clinic gives discounts (before the discount-capable charge flow is built).

### Q-ORT-4
- **AREA:** Orthodontics / finance
- **QUESTION:** Which role enters the orthodontic total price, deposit and number of months?
- **WHY IT MATTERS:** These terms create the ortho charge, the schedule and the installments.
- **WHAT IT BLOCKS:** Creating the ortho charge, generating the installment schedule, ortho reminders.
- **CURRENT SYSTEM BEHAVIOR:** `Orthodontics.Terms.Set` exists internally, assigned to **no role**; **no route is mapped**. The ortho case (area and appliance) can be created without terms; `charge_id`, `deposit_amount` and `number_of_months` are nullable until terms exist. The case has its one work item; no charge exists.
- **WHEN IT MUST BE ANSWERED:** Before the Orthodontics module's schedule generation is implemented.

### Q-FIN-6 (redefined)
- **AREA:** Finance / fixed lab
- **QUESTION:** How does a Fixed Lab revision (e.g. 200 → 300) affect Lab/Clinic completions that were already recorded: leave earlier completions unchanged and apply the new amount going forward, create corrections for earlier completions, or another rule?
- **WHY IT MATTERS:** It decides whether historical Lab/Clinic splits change after a Manager edit.
- **WHAT IT BLOCKS:** Nothing today; it matters only once Q-FIN-1 produces fixed-lab completions.
- **CURRENT SYSTEM BEHAVIOR:** **Resolved part:** a revision can **never** change a recognized Doctor Share. Revisions and completions are insert-only, so any rule can be applied later without rewriting rows. No fixed-lab completions exist yet.
- **WHEN IT MUST BE ANSWERED:** Together with Q-FIN-1.

### Q-FIN-9
- **AREA:** Finance / attribution
- **QUESTION:** Who earns collections on a charge after the patient is reassigned to another Doctor, or the performing Doctor becomes inactive? Which payroll period does a later-recognized earning belong to? May a hold be released if the patient is reassigned back?
- **WHY IT MATTERS:** Financial ownership generally follows the Doctor's performed work (confirmed), but long-running cases (ortho, implants) keep collecting after a change.
- **WHAT IT BLOCKS:** Doctor Share, payroll and revenue recognition for **held allocations only**.
- **CURRENT SYSTEM BEHAVIOR:** When a reassignment or deactivation happens while the charge has Outstanding > 0, an attribution hold is recorded. Payments, allocations and Outstanding are never blocked. Held allocations get a pending-attribution row, **no Doctor Share (not even provisional), no earning, no payroll entry, no Clinic revenue**. The Doctor's view shows a neutral "Share pending" marker; the Manager report shows Pending Doctor Attribution. Holds are never removed automatically.
- **WHEN IT MUST BE ANSWERED:** Before Payroll is completed and before the first patient reassignment with an open balance occurs in production.

### Q-PAY-1
- **AREA:** Finance / refunds and corrections
- **QUESTION:** Are refunds supported? Who authorizes them and how do they affect Doctor, Lab and Clinic shares already distributed (including Doctor payroll)? What is the procedure for an incorrectly recorded payment?
- **WHY IT MATTERS:** A reversal flows into the next Doctor payroll as a negative amount, which silently decides that a Doctor's share is clawed back; that is a business rule.
- **WHAT IT BLOCKS:** Any refund, payment reversal or charge adjustment endpoint.
- **CURRENT SYSTEM BEHAVIOR:** Primitives only (negative payment with `reverses_payment_id`, negated earning/completion, `charge_adjustments`), as a tested internal service. `Finance.Adjustments.Create` is assigned to the Manager but **no endpoint is mapped**.
- **WHEN IT MUST BE ANSWERED:** Before any correction capability is built; at the latest before go-live if the clinic expects to correct mistakes in-app.

### Q-PAY-2
- **AREA:** Payroll
- **QUESTION:** What is the payroll period (monthly, semi-monthly, other) for Doctor, Sales **and Assistant** payroll (v1.1), and what are the payroll status values and transitions?
- **WHY IT MATTERS:** Payroll aggregates recognized earnings per period; the user confirmed status / finalized-at / paid-date fields but not the period or the status list.
- **WHAT IT BLOCKS:** Period-driven draft creation defaults and the status workflow beyond the placeholders.
- **CURRENT SYSTEM BEHAVIOR:** The period is a parameter (any start/end date; Assistant payroll stores `period_start` / `period_end`, with overlapping periods prevented per Assistant). Status placeholders Draft, Finalized, Paid are used (technical).
- **WHEN IT MUST BE ANSWERED:** Before the Payroll module's workflow screens are completed.

### Q-PAY-3
- **AREA:** Finance / payments
- **QUESTION:** How are overpayments, unallocated amounts and patient credit handled?
- **WHY IT MATTERS:** The schema permits unallocated credit, but no policy exists.
- **WHAT IT BLOCKS:** Relaxing the two V1 validation defaults.
- **CURRENT SYSTEM BEHAVIOR:** `RecordPayment` requires Σ allocations to equal the payment amount, and refuses an allocation that would push a charge's Collected above its Billed (veto-able technical defaults).
- **WHEN IT MUST BE ANSWERED:** Before go-live if the clinic ever accepts advance payments.

### Q-PAY-ASST-1 *(v1.1)*
- **AREA:** Assistant Payroll / Reporting
- **QUESTION:** How should monthly Assistant salary expense be recognized / allocated across Daily, Weekly and Custom reporting periods? (Options raised and NOT decided: calendar-day proration, working-day proration, recognition only when payroll is finalized, recognition on the payment date, another clinic rule.)
- **WHY IT MATTERS:** The Manager financial reports need a period-specific operating result, but a monthly salary cannot be divided arbitrarily without a confirmed business rule. This is a *reporting-recognition* question only.
- **WHAT IT BLOCKS:** Accurate Assistant salary expense in non-monthly / custom reports; finalizing the Daily / Weekly / Custom Operating Result.
- **CURRENT SYSTEM BEHAVIOR:** Assistant payroll stores the salary for its payroll period. No daily / weekly proration is performed and salary is never divided by 30. If the report range fully contains a payroll period, that payroll is included normally. If the range only partially overlaps a payroll period, the Assistant Payroll Expense line shows **PENDING / NOT ALLOCATED FOR THIS REPORT RANGE** and the result is labelled **INCOMPLETE — ASSISTANT SALARY ALLOCATION PENDING**. *(TECHNICAL DEFAULT, veto-able: while this is open only Finalized and Paid payrolls count; Draft payrolls are listed as not yet finalized.)*
- **WHEN IT MUST BE ANSWERED:** Before Daily / Weekly / Custom Operating Result reporting is finalized.

### Q-PAY-ASST-2 *(v1.1)*
- **AREA:** Assistant Payroll / Salary Calculation
- **QUESTION:** How should Assistant salary be calculated when the employee works only part of a payroll period, joins or leaves during the period, or their salary changes during the period? (Examples: 1–14 March at a 6,000 monthly rate and 15 March onward at 7,000; or an Assistant who joins on 20 March.)
- **WHY IT MATTERS:** `final_amount` currently equals `salary_snapshot`, which only works cleanly when one salary applies to the full payroll period. This is a *payroll-amount calculation* question, separate from Q-PAY-ASST-1.
- **WHAT IT BLOCKS:** Partial-period Assistant payroll and payroll covering a mid-period salary change.
- **CURRENT SYSTEM BEHAVIOR:** The system does NOT prorate salary and does not allow an arbitrary `final_amount`. A payroll that requires a partial-period calculation cannot be finalized until the rule is confirmed. The model holds `current_salary` plus payroll snapshots plus audited salary changes; no effective-dated salary-history table exists yet and may be added if this answer requires it.
- **WHEN IT MUST BE ANSWERED:** Before the clinic needs a partial-period Assistant payroll or a salary change effective inside an active payroll period.

*Not a numbered question:* bonuses, deductions, overtime, attendance deductions, commissions and incentives for Assistants are **not confirmed and not designed**. If any exists it needs the owner's explicit approval before any schema is added.

### Q-FIN-DC
- **AREA:** Finance / reporting
- **QUESTION:** What is "Daily Closing" (it appears in the original Finance module list with no definition)? Is it needed?
- **WHY IT MATTERS:** A closing workflow would add a sealed daily state.
- **WHAT IT BLOCKS:** Any daily-closing feature.
- **CURRENT SYSTEM BEHAVIOR:** Nothing is designed. The Manager's daily dashboard computes daily totals live.
- **WHEN IT MUST BE ANSWERED:** Before Reporting is completed, if wanted.

## Orthodontics

### Q-ORT-1
- **AREA:** Orthodontics
- **QUESTION:** Initial generated installments are equal (the final installment absorbing any rounding difference). May individual installment amounts later be customized, and by whom?
- **WHY IT MATTERS:** Decides what the schedule UI offers.
- **WHAT IT BLOCKS:** Custom-schedule UI and edit rules.
- **CURRENT SYSTEM BEHAVIOR:** Installments are individual rows, so custom amounts are supported by the data model. Generation produces equal initial installments (the final installment absorbs the rounding difference) from a given first due date. Customization is **not confirmed** and no edit route exists.
- **WHEN IT MUST BE ANSWERED:** Before the Orthodontics schedule screens are completed.

### Q-ORT-2
- **AREA:** Orthodontics / finance
- **QUESTION:** How are payments applied to installments when a patient pays early, pays several future installments at once, or pays more or less than the amount due?
- **WHY IT MATTERS:** The confirmed installment fields (amount paid, payment date, method, received by, remaining) and statuses (Partially Paid, Paid, Overdue) depend on it.
- **WHAT IT BLOCKS:** Per-installment amount paid/remaining/status; any installment-application behavior.
- **CURRENT SYSTEM BEHAVIOR:** **Not resolved through the API.** Payments are allocated to the ortho **charge** only; no route, job or handler creates an `installment_payments` row (table and allocation-cap trigger exist, unused). Installment reads show due date and amount due, charge-level Collected and Outstanding, an "unapplied ortho collections" figure, and `applicationState: "Pending"`. Doctor Share is unaffected (computed at allocation time). J2 reminds while the ortho charge has Outstanding > 0.
- **WHEN IT MUST BE ANSWERED:** Before the Orthodontics installment display and reminders are completed.

### Q-ORT-3
- **AREA:** Orthodontics
- **QUESTION:** If the orthodontic total price changes mid-treatment (Manager correction), how does the remaining schedule change?
- **WHY IT MATTERS:** Charges are immutable; a price change needs an adjustment and a schedule rule.
- **WHAT IT BLOCKS:** Ortho price corrections.
- **CURRENT SYSTEM BEHAVIOR:** No route; schedule changes would be controlled, audited actions on `ortho_installments`; none is exposed.
- **WHEN IT MUST BE ANSWERED:** Before any ortho price-correction capability is built.

## Clinical

### Q-CLN-1
- **AREA:** Clinical / prescriptions
- **QUESTION:** After a visit is finalized, who may correct a prescription (and other parts of the finalized visit), and how? Is the visit or case reopened, or is there another mechanism?
- **WHY IT MATTERS:** Only "changes after finalization are auditable" is confirmed.
- **WHAT IT BLOCKS:** Any post-finalization correction endpoint or workflow.
- **CURRENT SYSTEM BEHAVIOR:** Before finalization the Doctor edits freely (own visit). After finalization the G1 trigger blocks changes for **everyone, including the Manager**, unless the transaction carries the audited-correction flag, which only the (not yet built) correction code path may set. No correction endpoint is mapped.
- **WHEN IT MUST BE ANSWERED:** Before the Clinical/Prescriptions modules are completed, if the clinic expects any post-finalization correction in-app.

### Q-CLN-2
- **AREA:** Clinical / work items
- **QUESTION:** When a Doctor continues another Doctor's case **without** separately priced work, should that work share any revenue?
- **WHY IT MATTERS:** Financial ownership generally follows the Doctor who performed the work (confirmed), but no rule describes shared or unpriced continuation.
- **WHAT IT BLOCKS:** Nothing; no work item is created for such work.
- **CURRENT SYSTEM BEHAVIOR:** Continuing a case adds a treatment record only; a new work item (with its own snapshot) exists only when `separatelyPricedWork = true`, and is refused on orthodontic cases. The continuing Doctor gets no financial ownership.
- **WHEN IT MUST BE ANSWERED:** Before Clinical is completed, if the clinic has such a scenario.

## Scheduling

### Q-APP-2
- **AREA:** Scheduling
- **QUESTION:** May the Manager intentionally book into a fully booked Doctor schedule, or is double-booking never allowed?
- **WHY IT MATTERS:** Decides whether an override path exists.
- **WHAT IT BLOCKS:** A Manager schedule-override feature.
- **CURRENT SYSTEM BEHAVIOR:** No override exists; the unique index refuses any same-slot booking for everyone.
- **WHEN IT MUST BE ANSWERED:** Before the Scheduling module is completed.

### Q-APP-3
- **AREA:** Scheduling / permissions
- **QUESTION:** May Sales also Cancel, Confirm or mark No Show on appointments, or is that Manager-only?
- **WHY IT MATTERS:** The confirmed status list has no stated actor for these changes.
- **WHAT IT BLOCKS:** Sales access to those status changes.
- **CURRENT SYSTEM BEHAVIOR:** `Scheduling.Appointments.UpdateStatus` is seeded to the Manager only; **Manager-only is the current seed, not a clinic rule**. Sales can reschedule but cannot cancel, confirm or mark no-show. Changing it is a one-line seed change.
- **WHEN IT MUST BE ANSWERED:** Before Scheduling UAT.

## CRM, patients and staff

### Q-APP-4 *(v1.1 scheduling correction)*
- **AREA:** Scheduling / Patient Arrival Workflow
- **QUESTION:** After removing the Arrived and Waiting appointment states, should the system track when a patient physically reaches the clinic? If yes: should Sales record it? Should it be an event or timestamp rather than a status? Should there be any Doctor queue? How should the Doctor know the patient is ready?
- **WHY IT MATTERS:** The previous workflow used Arrived and Waiting to represent the handoff between Reception and Doctor. Those states are now explicitly removed.
- **WHAT IT BLOCKS:** Any future patient-arrival / clinic-queue workflow.
- **CURRENT SYSTEM BEHAVIOR:** There is no Arrived state, no Waiting state, no Sales check-in action, no arrival columns, no waiting queue and no Doctor queue route. An appointment remains Scheduled or Confirmed until the Doctor starts the linked visit, at which point it becomes In Treatment.
- **WHEN IT MUST BE ANSWERED:** Before implementing any patient-arrival or Doctor waiting-queue feature. Arrived and Waiting are not reintroduced when this is answered unless the owner explicitly changes this requirement.

### Q-PAT-1
- **AREA:** Patients / CRM attribution
- **QUESTION:** Who is the Original Sales Employee of a walk-in patient registered directly, and who may assign or change it later?
- **WHY IT MATTERS:** Commission entitlement is associated with the patients an employee brought (confirmed), but direct registration is not a lead.
- **WHAT IT BLOCKS:** Attribution of walk-ins; any assign/change route.
- **CURRENT SYSTEM BEHAVIOR:** Direct registration leaves `original_sales_employee_id` **NULL** (Sales cannot supply it; Manager registration may supply an optional value). Lead conversion preserves the lead's value (confirmed). No change route exists. Reports show a count of patients without an Original Sales Employee.
- **WHEN IT MUST BE ANSWERED:** Before Q-FIN-2 is implemented / before Sales payroll is completed.

### Q-EMP-1
- **AREA:** Employees
- **QUESTION:** When a Sales employee becomes inactive, who handles their open leads and follow-ups, and does their commission entitlement on patients they brought continue?
- **WHY IT MATTERS:** Departures are confirmed (login disabled, history preserved) but ownership of open work is not.
- **WHAT IT BLOCKS:** Reassignment of leads/follow-ups; delivery of reminders for inactive owners.
- **CURRENT SYSTEM BEHAVIOR:** Nothing is moved. Follow-ups of an inactive owner are visible as "owner inactive" to the Manager and counted by J7 (digest only). No notification is sent to an inactive owner.
- **WHEN IT MUST BE ANSWERED:** Before Crm is completed.

## Other modules

### Q-EXP-1
- **AREA:** Expenses
- **QUESTION:** Do clinic operating expenses require a receipt or document attachment?
- **WHY IT MATTERS:** It would introduce file storage and file backup.
- **WHAT IT BLOCKS:** Any upload capability.
- **CURRENT SYSTEM BEHAVIOR:** No file storage exists in V1.
- **WHEN IT MUST BE ANSWERED:** Before the Expenses module is completed.

### Q-EXP-2
- **AREA:** Expenses
- **QUESTION:** What triggers finalization of an expense entry (after which only adjustment rows are allowed)?
- **WHY IT MATTERS:** The user confirmed "finalized entries use adjustment/reversal" but not what finalizes one.
- **WHAT IT BLOCKS:** Setting `finalized_at`.
- **CURRENT SYSTEM BEHAVIOR:** `finalized_at` is never set; expenses stay audited-editable; adjustment rows are available.
- **WHEN IT MUST BE ANSWERED:** Before the Expenses module is completed.

### Q-FDB-1
- **AREA:** Feedback
- **QUESTION:** After how long does a feedback request become Expired?
- **WHY IT MATTERS:** Without it the confirmed status never occurs.
- **WHAT IT BLOCKS:** Job J6.
- **CURRENT SYSTEM BEHAVIOR:** `expires_at` stays NULL; J6 is inert; nothing expires.
- **WHEN IT MUST BE ANSWERED:** Before the Feedback module is completed.

### Q-FDB-2
- **AREA:** Feedback
- **QUESTION:** How does the system learn that the patient submitted on the clinic website: a token-based public endpoint, or manual marking by Sales?
- **WHY IT MATTERS:** Decides whether the sixth anonymous endpoint exists.
- **WHAT IT BLOCKS:** The public submission route.
- **CURRENT SYSTEM BEHAVIOR:** V1 flow is manual (Sales issues a link and marks it sent). The columns support both options; `POST /api/public/feedback/{token}` exists only if the token option is chosen.
- **WHEN IT MUST BE ANSWERED:** Before the Feedback module is completed.

### Q-DOC-1
- **AREA:** Documents
- **QUESTION:** Are printed receipts or printed invoices needed, and must they be tax-compliant?
- **WHY IT MATTERS:** Adds a print model, numbering and possibly legal requirements.
- **WHAT IT BLOCKS:** Any receipt/invoice print endpoint.
- **CURRENT SYSTEM BEHAVIOR:** Payments return data only (`PaymentConfirmationDto`); nothing printable is defined.
- **WHEN IT MUST BE ANSWERED:** Before go-live if receipts are expected.

### Q-DOC-2
- **AREA:** Documents
- **QUESTION:** Are digital patient consent forms required?
- **WHY IT MATTERS:** Would add documents, signatures and retention requirements.
- **WHAT IT BLOCKS:** Any consent-form feature.
- **CURRENT SYSTEM BEHAVIOR:** None exists.
- **WHEN IT MUST BE ANSWERED:** Before go-live if required.

---

# GROUP 3: NON-BLOCKING BEFORE V1 GO-LIVE

### Q-PRV-1
- **AREA:** Privacy / Patients
- **QUESTION:** Should Sales see the Doctor's referral reason, and should DOB visibility to Sales be changed from its current setting?
- **WHY IT MATTERS:** Visibility was deliberately not locked by the client; it is a privacy setting.
- **WHAT IT BLOCKS:** Nothing; it is a configuration value.
- **CURRENT SYSTEM BEHAVIOR:** Referral reason is hidden from Sales (configuration setting, unset = hidden). No code change is needed to change it.
- **WHEN IT MUST BE ANSWERED:** Before V1 go-live.

### OPS-1
- **AREA:** Operations / backup security
- **QUESTION:** Who holds the two backup recovery private keys, and where are the copies stored?
- **WHY IT MATTERS:** A backup contains password hashes, the audit log and every clinical and financial record. Losing both keys makes every off-site backup unreadable. Either key alone decrypts (redundancy, **not** two-person approval).
- **WHAT IT BLOCKS:** Configuring the two public keys in production; go-live.
- **CURRENT SYSTEM BEHAVIOR:** Encryption to two independent recipients is designed; no private key is on the VM; custody is **not** tied to any application role (the Manager role does not automatically hold a key). Public keys are deployment configuration.
- **WHEN IT MUST BE ANSWERED:** Before go-live (before the first production backup).

### OPS-2
- **AREA:** Legal / data residency / retention
- **QUESTION:** Where may Egyptian patient data be hosted and backed up (region, cross-border rules)? How long must medical and financial records be retained, and must any controlled-erasure workflow also reach backups? Is hosted error tracking acceptable?
- **WHY IT MATTERS:** Determines the production region, the off-site backup provider, retention numbers and the error-tracking choice.
- **WHAT IT BLOCKS:** Choosing the production region and providers.
- **CURRENT SYSTEM BEHAVIOR:** Nothing assumed. The architecture runs in any region that meets the provider criteria. Backup retention numbers are defaults. No erasure workflow is designed.
- **WHEN IT MUST BE ANSWERED:** Before the production region and providers are chosen, and before go-live.

### OPS-3
- **AREA:** Operations / hosting
- **QUESTION:** Which cloud provider, region, domain and managed PostgreSQL product (meeting the Step 12 criteria, including PITR granularity)?
- **WHY IT MATTERS:** Needed to provision staging and production; PITR granularity decides whether the 5-minute RPO claim can be made.
- **WHAT IT BLOCKS:** Provisioning staging/production.
- **CURRENT SYSTEM BEHAVIOR:** Vendor-independent design; Development runs on Docker Compose.
- **WHEN IT MUST BE ANSWERED:** Before staging is provisioned.

### OPS-4
- **AREA:** Operations / recovery targets
- **QUESTION:** Does the Manager accept the proposed RPO/RTO (A: none/4 h, B: 5 min/4 h, C: 24 h/1 day), the retention defaults, and the manual paper fallback process during downtime?
- **WHY IT MATTERS:** These are risk figures the clinic should consciously accept.
- **WHAT IT BLOCKS:** Nothing in code.
- **CURRENT SYSTEM BEHAVIOR:** The defaults are designed and tested as written.
- **WHEN IT MUST BE ANSWERED:** Before go-live.

### OPS-5
- **AREA:** Operations / on-call
- **QUESTION:** Who is the named operator and the backup contact for critical alerts, and which monitoring vendors are used?
- **WHY IT MATTERS:** One freelancer is a single point of failure for alerts.
- **WHAT IT BLOCKS:** Alert routing configuration.
- **CURRENT SYSTEM BEHAVIOR:** Routing is designed (critical: SMS/push + email, escalation after 30 minutes); vendors are examples, not decisions.
- **WHEN IT MUST BE ANSWERED:** Before go-live.

### Q-BRN-1
- **AREA:** Scope
- **QUESTION:** Is multi-branch needed in Version 1?
- **WHY IT MATTERS:** Decides whether a branch dimension is added to the schema.
- **WHAT IT BLOCKS:** Nothing now; adding a branch column later is a migration.
- **CURRENT SYSTEM BEHAVIOR:** Out of scope; single clinic; no branch field.
- **WHEN IT MUST BE ANSWERED:** Before go-live planning, ideally before the schema is first deployed to production.

---

## Items that are NOT open (for the record)

- Q-FIN-6's first meaning (does a Fixed Lab revision change a recognized Doctor Share): **resolved, NO**.
- Address and Original Sales Representative are hidden from Doctors; the Sales restriction is mandatory; Doctors have no notification inbox in V1; one operational role per account; Doctors collect no payments.
- Inventory and X-ray are removed from scope and must not appear anywhere.

## Technical defaults that are not questions (may be vetoed)

Session timeouts, password policy, lockout and rate limits, reminder planner intervals and availability window, retry cap 5, `Africa/Cairo` default timezone, V1 payment validation defaults, Doctor follow-up booked with the authoring Doctor, 30-day log ceiling, sizing defaults.
