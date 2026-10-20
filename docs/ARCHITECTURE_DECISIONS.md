# ARCHITECTURE_DECISIONS.md

**Design Baseline Version:** 1.1
**Status:** OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION (approved 2026-10-20)

> **Freeze note.** ADR-001 to ADR-018 belong to the Owner-approved, frozen Design Baseline v1.1 (2026-10-20). A future architectural change that alters a frozen business rule requires a new approved baseline revision; an implementation detail that does not change business behavior does not.

Eighteen architecture decision records, synchronized with the Owner decisions of the Owner Review pass and the final Owner business changes (see DESIGN_FREEZE.md). ADR-017 was added in the final business-change pass; ADR-018 (Sales commission rate history) is the only record added in the final correction pass. Each is **TECHNICAL**: none adds business behavior. A decision that touches a business rule says which confirmed rule it implements. Changing an ADR after the freeze requires an explicit, user-approved change to the design baseline.

---

## ADR-001: Modular monolith, one database, one DbContext, one schema per module

**Decision.** One codebase deployed as two processes (`Clinic.Api`, `Clinic.Worker`), one PostgreSQL database, one EF Core DbContext, one PostgreSQL schema per module. A module is one project with internal folders (Domain / Application / Infrastructure / Contracts); its public surface is its `Contracts` namespace, everything else is `internal`. Communication is in-process: contract calls, in-process domain events, and background jobs. No message broker.

**Reason.** The user confirmed "modular monolith; no microservices, Kafka, RabbitMQ, event sourcing, Kubernetes or complex CQRS". One DbContext allows one request to be one transaction (payment + allocation + earning + completion). One project per module (not per layer) gives compile-time boundaries without ~60 projects. The Worker is a second host of the same modules so a slow job can never slow front-desk requests.

**Consequences.** Cross-module transactions are easy and atomic. Ownership of tables is enforced by schema naming, migration naming and an architecture test, not by separate DbContexts. A module split later would require extracting its schema, which the schema-per-module layout keeps feasible. 16 implementation modules cover the 22 confirmed modules. Hand-written dispatchers avoid a commercial-licence dependency.

---

## ADR-002: Strict module levels; Finance never calls Clinical

**Decision.** (Clarification: the Staff module holds Doctor, Sales / Reception and Assistant records, plus Manager-maintained attendance and Assistant overtime; Assistant is a staff type with no account, no login and no application role, so module count, levels and dependencies are unchanged.) Seven levels (L1 Audit/Configuration/Identity; L2 Staff/Notifications/Expenses; L3 Patients; L4 Scheduling/Crm/Finance; L5 Clinical; L6 Prescriptions/Orthodontics/Feedback; L7 Payroll/Reporting). A module may depend only on a lower level's Contracts, or on events published by a lower module. Finance is a downstream sink: **Finance subscribes to no events and never calls Clinical.** At Visit finalization, Clinical (a higher level) calls the Finance contract (create charges from Price − Discount per pricing line, where the discount was set by the Doctor, Sales or Manager and is read from Clinical's own `pricing_discount_entries`, ADR-017) and the Scheduling contract (release unused booked time), downward, in the same database transaction. Notifications defines ports (`INotificationSourceValidator`, `INotificationRenderer`, `INotificationChannel`) that higher modules implement. Reporting is the one controlled exception: read-only queries across modules; nothing depends on it.

**Reason.** Prevents cycles; keeps the ledger independent of clinical workflow so it can be tested and reasoned about alone.

**Consequences.** A build-time architecture test (NetArchTest) fails any upward dependency. Some data is copied once by design (Doctor percentage from work item to entitlement); an equality test guards the copy. The former `PatientReassigned` / `DoctorDeactivated` Finance handlers are **removed** (no attribution holds exist; Q-PAT-1/Q-EMP-1). In V1 Finance events are consumed only by Orthodontics (Crm and Notifications consume none).

---
## ADR-003: Keep database foreign keys across modules; avoid cross-module EF navigation

**Decision.** Every reference to a business entity is a real PostgreSQL foreign key, even across schemas, `ON DELETE NO ACTION`. EF mapping uses scalar id properties and one-sided `HasOne<Other>().WithMany().HasForeignKey(...)` declared in one file, `CrossModuleReferences.cs`; an architecture test compares that file with the database. No duplicate copy of an entity is created to avoid a dependency. Polymorphic references without FKs are limited to `audit.audit_log.entity_id` and `notifications.related_entity_id`.

**Reason.** The user accepted "ids instead of cross-module navigation" as a modularity technique but required PostgreSQL referential integrity ("correctness over artificial isolation").

**Consequences.** Composite foreign keys carry patient/case/charge consistency into the database (e.g. a payment and its charge cannot belong to different patients). FK direction is free; only code dependencies follow module levels. Migrations that add cross-module FKs are named `<date>_Xref_<Change>`.

---

## ADR-004: Audience-segmented features and role-specific DTOs make privacy structural

**Decision.** Endpoints, handlers and DTOs are separated by audience (`Features/Doctor`, `Features/Sales`, `Features/Manager`). Contact data lives in a separate table and behind a separate contract; Doctor code cannot obtain the contact interface. Every response is a projection into an audience DTO; entities are never serialized. One OpenAPI document and generated TypeScript client per audience. Contact-bearing types are tagged `[ContactData]`; architecture tests, OpenAPI property scans and canary integration tests enforce the boundary. Sales reads clinical data only through a whitelist summary contract, and reads or sets pre-finalization prices and discounts only through the pricing contract and `PricingLineSalesDto` (ADR-017). Referral `notes` exist on the Doctor DTO only; `SalesReferralDto` has no such property. Optional defense in depth: a read-only database role for Doctor endpoints with no access to contact tables.

**Reason.** Confirmed: backend-enforced privacy; React-only hiding is unacceptable; Doctors never see contact data; Sales must not see detailed Doctor-authored clinical data (v1.1 clarification: Sales / Reception collects and accesses the structured Patient Medical History Intake through its own audience DTO and permission keys, as a deliberate narrow exception).

**Consequences.** More types (one DTO per audience) in exchange for impossibility of accidental leaks. Sales-authored text that a Doctor reads (appointment `reason`) remains a heuristic leak path (phone/email pattern check).

---

## ADR-005: Cookie-session authentication with a stamp check on every request

**Decision.** ASP.NET Core Identity with an HttpOnly, Secure, `SameSite=Strict`, `__Host-` cookie; the security stamp and `is_active` are verified against the database on every request; permissions are loaded per request from a cached role map; anti-forgery token on every unsafe method plus `Origin`/`Sec-Fetch-Site` verification; one origin through the reverse proxy, so no production CORS. Data Protection keys live in `infra.data_protection_keys`, encrypted by a certificate present only in the `api` container.

**Reason.** Confirmed: disabling an employee's login must work immediately; CSRF protection is required. Cookie sessions avoid tokens in browser storage and allow immediate kill by rotating the stamp.

**Consequences.** A cookie copied before logout stays valid until expiry or stamp rotation (accepted trade-off; logout rotating the stamp is a one-line option). A restore returns old password hashes, so a mandatory lockdown procedure (ADR-015) exists; passwords are set by the Manager (no reset tokens). The Worker does not authenticate browser sessions and has no access to the key table.

---

## ADR-006: One operational role per account; permissions as data changed only by migration

**Decision.** `identity.user_roles` is unique on `user_id`; permissions are the single role's set (no union). Permission keys follow `<Module>.<Resource>.<Action>[.<Scope>]` and are mapped to roles by reviewed deployment migrations (no in-app role editor in V1). Contact-bearing keys can never map to DOCTOR (migration and startup tests). Every permission key is assigned to at least one role (e.g. `Finance.Discount.Set` to Doctor, Sales and Manager; `Expenses.CreateOperational` to Sales and Manager; `Staff.Attendance.Manage`, `Staff.AssistantOvertime.Manage`, `Staff.AssistantOvertimeRate.Configure`, `Crm.Responsibility.Assign` and `Feedback.Results.Read` to Manager only; `Feedback.Entries.Record` to Sales and Manager; `Finance.FixedLab.SetInitial`, `Orthodontics.Terms.SetInitial` and `Clinical.WorkItems.EnterPrice` (the **initial** entries) to the Doctor only, `Finance.FixedLab.Revise`, `Orthodontics.Terms.Edit`, `Orthodontics.Installments.Edit`, `Staff.SalesCommission.Configure` and `Finance.Charges.Adjust` (revisions and corrections) to the Manager only, `Expenses.Categories.ReadAllowed` to Sales and Manager); a mapped route whose key has no role fails the build. Composite policies (`HasAllPermissions`) express "both keys required" (patient registration).

**Reason.** Confirmed: a Doctor account must never gain contact access through another role. A union of roles would make that guarantee depend on every role combination. Open business decisions must not become silently reachable.

**Consequences.** A person needing Doctor and Sales functions needs separate accounts or an explicitly designed role definition. Role changes are Manager actions that rotate the stamp. Two technical Manager-only keys (`Scheduling.Appointments.UpdateStatus`, `Reports.Export`) were added because the confirmed matrix had no actor for appointment status changes and each export has its own key; both are seeds, not clinic rules.

---

## ADR-007: Append-only ledger with Patient Credit; Billed, Collected and Outstanding are derived

**Decision.** `patient_charges`, `payments` (kind Receipt / Reversal / Refund), `payment_allocations` (signed; `allocation_seq`, `allocated_at`, `allocation_source`), `patient_credit_entries`, `payment_corrections`, `doctor_entitlements`, `doctor_earnings`, `revenue_distributions`, `distribution_events`, fixed-lab tables and adjustments are insert-only (the application role has no UPDATE or DELETE grant). No stored balance exists. Money is `numeric(12,2)`, arithmetic in `decimal`, API money as strings. `RecordPayment` is one transaction with a client idempotency key (unique), ordered row locks on charges, and allocation to charges that is manual (Sales) with an unallocated remainder becoming **Patient Credit** (consumed FIFO). Guard **G8**: payment amount = Σ allocations + Σ credit entries. Corrections (Reversal, Refund, Reallocation, Replacement) are **Manager-only** `payment_corrections` and always new rows; the earlier corrections propagate to payroll as delta rows in the current open payroll month. Guard **G8b**: lab + clinic + doctor earnings equal the allocation basis, and 0 ≤ Σ earnings ≤ Σ entitlements. There is no Walk-In pool and no pool guard: Walk-In / Direct work earns zero Sales commission, and Sales commission is computed on money actually collected (after discount, after corrections).

**Reason.** Confirmed: financial history is never overwritten; Charge and Payment are separate; Billed/Collected/Outstanding are never conflated; duplicate-payment protection; Patient Credit, payment corrections and Manager adjustments are confirmed (Q-FIN-W1/W2, Q-PAY-1…3).

**Consequences.** Every figure is a query over immutable rows (exact and reproducible). Rounding: half-up for Doctor share and general lab; Clinic Share is the remainder so the parts sum exactly (database CHECK).

---
## ADR-008: Per-Doctor work items and percentage snapshots carry financial ownership

**Decision.** Financial ownership is a property of `clinical.case_work_items` (performing Doctor, Price entered by that Doctor, percentage snapshot read once from the Doctor's current percentage), not of the treatment case. A treatment case may have many work items and many charges (one per work item, created at Visit finalize with Price − Discount, fixed EGP amount; the discount may have been set by the Doctor, Sales or Manager and its setter and role are copied to the Charge). Charges carry write-once copies of Doctor, case and percentage, protected by composite FKs and an equality test. Doctor-priced work is per Doctor; an orthodontic case still produces its single ortho charge. Continuing a case never needs a special flag: each Doctor prices their own work and keeps the financial ownership of it after any patient reassignment. Patient–Doctor assignment history is separate and never referenced by historical work.

**Reason.** Confirmed: financial ownership follows the Doctor's performed work and percentage snapshot; patient assignment and financial ownership are distinct; the percentage is snapshotted when the work begins and never recalculated; multiple Doctors may work one case, each pricing their own work; Doctor, Sales and Manager may apply a fixed discount before finalization (Q-FIN-3, final Owner change); the money stays owned by the performing Doctor.

**Consequences.** Reassignment or Doctor deactivation cannot move history; there are no attribution holds. The work item is a lifecycle table frozen by trigger G6.

---
## ADR-009: Entitlement is separate from recognition; general lab is immediate; fixed lab is Lab-First

**Decision.** At charge creation `doctor_entitlements` records the maximum Doctor Share (net charge × snapshot %). For every allocation: **general-lab** work recognizes `doctor_share`, lab (rate at allocation time) and clinic immediately in `doctor_earnings` / `revenue_distributions`; **fixed-lab** work follows a deterministic **Lab-First waterfall** (Lab recovered first, then Doctor entitlement via a FIFO pending-entitlement queue, then Clinic), executed by an idempotent Reconcile that emits only delta rows; the entitlement ledger is proven by replay: a Manager revision of the lab amount changes recognition, never entitlement, and a payment that does not yet cover the lab leaves the Doctor pending, not negative and not the Clinic. `doctor_earnings.recognized_at` fixes the payroll month. `CHECK doctor_share + lab + clinic = allocated basis` (G8b); Clinic Share is never negative. There is no pending-attribution row and no lab-rate "all candidates agree" gate.

**Reason.** Confirmed: Collected = Doctor Share + Lab + Clinic (Q-FIN-W1/W2/1/2), with the Lab-First rule for fixed-lab work.

**Consequences.** Fixed-lab Doctor payroll begins only once Lab is recovered; Reconcile after any revision/correction is deterministic and replayable. Later calculators insert rows under a new `rule_version` without rewriting history.

---
## ADR-010: One fixed lab obligation per case, as revisions; revisions reconcile by delta rows

**Decision.** `finance.fixed_lab_expenses` has exactly one row per fixed-lab treatment case (unique `treatment_case_id`, insert trigger G7 requiring `procedure_types.lab_model = 'Fixed'`), and `finance.fixed_lab_revisions` hangs off it (revision 1 = the Doctor's initial entry, `Finance.FixedLab.SetInitial`, made together with the price of the Fixed / Removable Prosthetic work and **required before the first Fixed-lab Treatment charge can be established** (guard G7), so an unknown Fixed Lab is never treated as 0; a Manager revision, `Finance.FixedLab.Revise`, inserts the next revision with a reason; a database CHECK ties revision 1 to the Doctor role and later revisions to the Manager role; every edit audited). A new revision triggers Reconcile, which re-evaluates the waterfall and inserts only the difference as delta rows. No snapshot of the amount is copied anywhere.

**Reason.** Confirmed: one fixed lab amount per case, however many Doctors or payments; the Doctor enters it, the Manager may revise it with full audit and can never be the initial actor.

**Consequences.** Past rows are never rewritten; the effect of a revision lands in the current open payroll month. General lab is untouched.

---
## ADR-011: Finalized-visit guard (G1) with controlled Doctor correction; lifecycle tables

**Decision.** Tables are classified insert-only (H), lifecycle with named mutable columns (L), or mutable audited (M). Visit-owned clinical records and prescriptions are editable while the visit is not finalized and are blocked after finalization by one shared trigger function (**G1**) unless the transaction carries `SET LOCAL clinic.audited_correction = 'on'` **and** a matching `clinical.visit_corrections` row exists. The controlled Doctor correction (Q-CLN-1/2) is the only path that sets it, writes the audit entry and links the correction. Payroll tables are sealed by **G2** (finalize only after month end; Draft→Finalized→Paid). **G3 is retired** (Expenses are never finalized, Q-EXP-1). Work items are frozen by G6. Column-level `UPDATE` grants protect frozen columns a second time.

**Reason.** Confirmed: history is preserved and post-finalization changes are auditable and attributable; pre-finalization editing and status transitions remain possible.

**Consequences.** The G1 flag guards against accidents, not a compromised application; it is set in one class (architecture-tested).

---
## ADR-012: Hangfire Worker; notifications with dedupe keys and per-item transactions

**Decision.** All time-based work runs only in the Worker (Hangfire, PostgreSQL storage; job arguments are ids only). Planners (J1–J3) live in the owning module (a follow-up with no responsible Sales employee is skipped by J3 and only counted by J7) and enqueue notifications with `ON CONFLICT (dedupe_key) DO NOTHING`; the key is `{type}:{entity_id}:{clinic-local target date}:{recipient_user_id}`. "Tomorrow" is a clinic-local calendar date computed through one `IClinicClock` (NodaTime, configurable timezone). The dispatcher (J4, every minute) processes each notification in its own transaction with `FOR UPDATE SKIP LOCKED`, commits a failure as `Failed` in a new short transaction, and never rolls back other items; retry (J5, cap 5) reuses the same row and key. The Worker resolves active Sales users and Sales-owner activity only through two minimal views behind Identity/Staff contracts. No Doctor receives notifications.

**Reason.** Confirmed: idempotent, retry-safe, timezone-aware, duplicate-resistant processing; Sales as recipients; no Doctor inbox. Least privilege keeps the Worker away from password hashes, contacts, clinical notes and staff compensation.

**Consequences.** Running a job twice is harmless; one poison notification never blocks others. A planner window of 15 minutes means an item created at 23:55 for "tomorrow" may miss its reminder if the next run is after midnight (accepted). There is no J6 (feedback has no expiry or reminder job); attendance and overtime generate no jobs and no notifications. J2 checks the ortho charge's Outstanding.

---

## ADR-013: API conventions; Arabic-only SPA; six anonymous endpoints

**Decision.** Audience prefixes with one OpenAPI document each; JSON with unknown members rejected; money as strings; RFC 7807 errors with stable codes and no echoed input; optimistic concurrency via ETag/`If-Match` (`xmin`), `Idempotency-Key` required on payments; search with names or phones is always a POST body; sort/filter whitelists; scoped resources return 404. The React + TypeScript SPA is **Arabic only, right-to-left**, single branch. Six anonymous endpoints only (including public-token feedback GET/POST, no expiry). Manager Set Password replaces any reset flow. Sales-entered feedback is a second, authenticated route that writes the same `feedback_entries` table with `source = SalesEntered`. The API has **no routes** for password reset tokens, receipts, consents, attachments, Daily Closing, branches, Inventory, X-ray, Walk-In pools, Assistant login or self-check-in.

**Reason.** Confirmed: concurrency safety and duplicate protection; backend authorization; privacy; Arabic RTL only (Q-UI-1); the rule against inventing capabilities.

**Consequences.** All pages are RTL; pagination, dates and money formatting follow one Arabic locale helper.

---
## ADR-016: Arrival event, overlap allowed, availability release

**Decision.** `scheduling.appointment_arrivals` (insert-only, Sales only) records patient arrival; the Doctor queue is derived from arrivals and statuses — there is no Arrived/Waiting/Check-In status. Appointments carry a manual `duration_minutes NOT NULL`; overlapping appointments are allowed. When the Visit is finalized (`visit_ended_at`), unused booked time is released through a view. Exactly seven appointment statuses; Sales and Manager may Confirm / Cancel / No Show.

**Reason.** Confirmed Q-APP-1…4 and Q-CLN scheduling decisions.

**Consequences.** No exclusion constraint on time ranges; the queue and availability are views over arrival and visit rows.

---

## ADR-014: Single-VM deployment with expand/contract migrations and compatibility-based readiness

**Decision.** One Linux VM runs Caddy (TLS, SPA, proxy), `api`, `worker`, plus one-shot `migrator` and `backup` containers; PostgreSQL is managed (container only for Development). CI/CD on GitHub Actions deploys the exact tested image digests (staging automatically, production by approved tag). The application never migrates itself; only the migrator (with `clinic_migrator`) changes the schema, using expand/contract so the previous release keeps working. `/health/ready` checks `required − applied` migrations (extra applied migrations are ignored), so an automatic application rollback against a newer additive schema stays Ready. Non-transactional migrations require an explicit marker, single purpose, retry-safe statements and an invalid-index pre-flight. A CI N-1 compatibility job proves the previous release works while the new migrator runs and after rollback. There is no automatic database downgrade.

**Reason.** Confirmed: Docker, Linux, HTTPS, cloud-hosted, managed PostgreSQL preferred, GitHub Actions, no destructive migration at startup, no Kubernetes. Operable by one person.

**Consequences.** One VM is a single point of failure (recovery by rebuild; data safe in the managed database). Only the immediately previous release is guaranteed rollback-compatible. Secrets are Docker secrets from a root-owned directory; the Data Protection certificate is only in `api`.

---

## ADR-015: Three-layer backups; the Manager (person) holds the keys; post-restore lockdown via Manager-set passwords

**Decision.** (1) Provider backups with PITR; (2) an independent nightly `pg_dump` by the `backup` container, client-side `age`-encrypted, uploaded with write-only credentials to object-locked storage at a different provider/account, with per-run verification and ledger integrity checks; (3) a pre-deploy restore point. Hosting: single branch, DigitalOcean / Frankfurt Droplet (about 2 vCPU / 4 GB, configurable, no prices in the design) + Managed PostgreSQL; **RPO 5 minutes, RTO 4 hours** for the realistic failures. Recovery-key custody: the Clinic Manager as a **person** holds both keys (a custody duty, not an application permission); the physical storage locations are **OPS-1 (DEFERRED)** — the design does not decide them. Operations contacts: primary operator = project developer / system owner; backup critical-alert contact = Clinic Manager. `clinic_backup` has explicit SELECT grants only (no access to `infra.data_protection_keys`). After any restore to an earlier point the operator runs `post-restore-lockdown`: rotate all security stamps and **lock every non-Manager account**; the Manager then re-establishes access with `set-manager-password` (break-glass) and Set Password per user. Restore drills use real data and never run in staging.

**Reason.** Confirmed: automated, encrypted, off-site, retained, restore-tested backups; least privilege; Manager-created accounts and Set Password with no reset tokens (Q-ACC-1, OPS-2…5).

**Consequences.** A restore signs everyone out until the Manager sets passwords. Honest exception: scenario C (provider or account loss) is outside the 5-minute RPO and 4-hour RTO unless off-site WAL archiving is added. Loss of all recovery keys makes off-site backups unreadable.

---

## ADR-017: Pre-finalization discount entries and an audience-safe pricing contract

**Decision.** The discount a patient is quoted before the Visit is finalized is stored as insert-only rows in `clinical.pricing_discount_entries` (one logical row per pricing line; current = highest `entry_seq`; fixed EGP amount, no percentage column; actor, role `DOCTOR` / `SALES_RECEPTION` / `MANAGER`, time). Each row references **exactly one** pricing line by a direct FK (`case_work_item_id → case_work_items(id)` or `consultation_visit_id → visits(id)`, XOR CHECK; no `visit_id` column and no composite FK, so the Visit is never stored twice). It lives in **Clinical** because it belongs to the Visit being worked on, which keeps **Finance from ever calling Clinical**. Sales / Reception reads and sets it through a pricing contract that returns only `PricingLineSalesDto` (patient name, procedure name, treating Doctor name, price, discount, total, `entrySeq`), never clinical notes, lab, Doctor percentage, entitlement or commission. The writer sends the `entry_seq` it saw; a unique constraint turns a concurrent second writer into a 409. Insert is allowed only while the Visit is not Finalized and the line has a price (guard G6); at finalization Clinical passes price, discount, setter, role and time to the Finance contract, which snapshots them on the Charge. After establishment, only the Manager changes a discount (a `DiscountCorrection` adjustment; never on an Orthodontic charge, whose Total changes only through `Orthodontics.Terms.Edit`).

**Reason.** Confirmed final Owner change: Doctor, Sales and Manager may set a discount. The alternative of putting a mutable discount field on the work item would lose the history and give Sales a write path into a clinical table; the alternative of a Finance-side discount table would make Finance depend on a Visit it must not know.

**Consequences.** One more insert-only table and one narrow DTO. Commission and Doctor share follow the money actually collected on the discounted Charge, so no special commission rule for discounts exists. Sales sees prices because it already quotes and collects them; it sees nothing else about the work.

---

## ADR-018: Sales commission rate history with a per-item snapshot (future collections only)

**Decision.** The Sales commission rate is stored once, in the insert-only, effective-dated `staff.sales_commission_rate_history` (revision 1 written in the same transaction that creates the Sales employee; each Manager change appends a revision whose `effective_from` is the database time of the save; no back-dating, no future-dating). `staff.sales_employees` has **no rate column**. A collection is commissioned at the revision with the greatest `effective_from ≤ allocated_at`. Each `payroll.sales_payroll_commission_items` row is per net payment allocation (unique `payment_allocation_id`) and **snapshots** `commission_percentage_applied` with the allocation reference and the amount; a reversal row reuses the rate of the allocation it reverses. A finalized payroll is immutable (G2). Walk-In / Direct patients have no Original Sales Employee, so no item and no pool exist.

**Reason.** Owner decision Q-COMM-1: the Manager may change the rate later and the new rate applies to future collections only. The earlier design snapshotted one percentage per payroll, which cannot represent a mid-month change and would invite recalculation with the current rate.

**Consequences.** One more small history table and a per-allocation commission item. Recalculation is impossible by construction (history is insert-only, items are unique per allocation and snapshotted). The Doctor percentage keeps its existing model (current value on the Doctor, snapshot on the work item).
