# ARCHITECTURE_DECISIONS.md

**Design Baseline Version:** 1.1
**Status:** PROPOSED FREEZE — AWAITING OWNER APPROVAL

Fifteen architecture decision records. Each is **TECHNICAL**: none adds business behavior. A decision that touches a business rule says which confirmed rule it implements. Changing an ADR after the freeze requires an explicit, user-approved change to the design baseline.

---

## ADR-001: Modular monolith, one database, one DbContext, one schema per module

**Decision.** One codebase deployed as two processes (`Clinic.Api`, `Clinic.Worker`), one PostgreSQL database, one EF Core DbContext, one PostgreSQL schema per module. A module is one project with internal folders (Domain / Application / Infrastructure / Contracts); its public surface is its `Contracts` namespace, everything else is `internal`. Communication is in-process: contract calls, in-process domain events, and background jobs. No message broker.

**Reason.** The user confirmed "modular monolith; no microservices, Kafka, RabbitMQ, event sourcing, Kubernetes or complex CQRS". One DbContext allows one request to be one transaction (payment + allocation + earning + completion). One project per module (not per layer) gives compile-time boundaries without ~60 projects. The Worker is a second host of the same modules so a slow job can never slow front-desk requests.

**Consequences.** Cross-module transactions are easy and atomic. Ownership of tables is enforced by schema naming, migration naming and an architecture test, not by separate DbContexts. A module split later would require extracting its schema, which the schema-per-module layout keeps feasible. 16 implementation modules cover the 22 confirmed modules. Hand-written dispatchers avoid a commercial-licence dependency.

---

## ADR-002: Strict module levels; Finance never calls Clinical

**Decision.** (Clarification, v1.1: the Staff module holds Doctor, Sales / Reception and Assistant records; Assistant is a staff type with no account and no application role, so module count, levels and dependencies are unchanged.) Seven levels (L1 Audit/Configuration/Identity; L2 Staff/Notifications/Expenses; L3 Patients; L4 Scheduling/Crm/Finance; L5 Clinical; L6 Prescriptions/Orthodontics/Feedback; L7 Payroll/Reporting). A module may depend only on a lower level's Contracts, or on events published by a lower module. Events flow from lower publisher to higher subscriber. Finance is a downstream sink: Clinical and Orthodontics pass the performing Doctor, percentage snapshot and lab model to Finance as data. Notifications defines ports (`INotificationSourceValidator`, `INotificationRenderer`, `INotificationChannel`) that higher modules implement. Reporting is the one controlled exception: read-only queries across modules; nothing depends on it.

**Reason.** Prevents cycles; keeps the ledger independent of clinical workflow so it can be tested and reasoned about alone.

**Consequences.** A build-time architecture test (NetArchTest) fails any upward dependency. Some data is copied once by design (Doctor percentage from work item to charge); an equality test guards the copy. Finance subscribes to `PatientReassigned` (Patients) and `DoctorDeactivated` (Staff); in V1 Finance events are consumed only by Orthodontics (Crm and Notifications consume none).

---

## ADR-003: Keep database foreign keys across modules; avoid cross-module EF navigation

**Decision.** Every reference to a business entity is a real PostgreSQL foreign key, even across schemas, `ON DELETE NO ACTION`. EF mapping uses scalar id properties and one-sided `HasOne<Other>().WithMany().HasForeignKey(...)` declared in one file, `CrossModuleReferences.cs`; an architecture test compares that file with the database. No duplicate copy of an entity is created to avoid a dependency. Polymorphic references without FKs are limited to `audit.audit_log.entity_id` and `notifications.related_entity_id`.

**Reason.** The user accepted "ids instead of cross-module navigation" as a modularity technique but required PostgreSQL referential integrity ("correctness over artificial isolation").

**Consequences.** Composite foreign keys carry patient/case/charge consistency into the database (e.g. a payment and its charge cannot belong to different patients). FK direction is free; only code dependencies follow module levels. Migrations that add cross-module FKs are named `<date>_Xref_<Change>`.

---

## ADR-004: Audience-segmented features and role-specific DTOs make privacy structural

**Decision.** Endpoints, handlers and DTOs are separated by audience (`Features/Doctor`, `Features/Sales`, `Features/Manager`). Contact data lives in a separate table and behind a separate contract; Doctor code cannot obtain the contact interface. Every response is a projection into an audience DTO; entities are never serialized. One OpenAPI document and generated TypeScript client per audience. Contact-bearing types are tagged `[ContactData]`; architecture tests, OpenAPI property scans and canary integration tests enforce the boundary. Sales reads clinical data only through a whitelist summary contract. Optional defense in depth: a read-only database role for Doctor endpoints with no access to contact tables.

**Reason.** Confirmed: backend-enforced privacy; React-only hiding is unacceptable; Doctors never see contact data; Sales must not see detailed clinical data.

**Consequences.** More types (one DTO per audience) in exchange for impossibility of accidental leaks. Sales-authored text that a Doctor reads (appointment `reason`) remains a heuristic leak path (phone/email pattern check).

---

## ADR-005: Cookie-session authentication with a stamp check on every request

**Decision.** ASP.NET Core Identity with an HttpOnly, Secure, `SameSite=Strict`, `__Host-` cookie; the security stamp and `is_active` are verified against the database on every request; permissions are loaded per request from a cached role map; anti-forgery token on every unsafe method plus `Origin`/`Sec-Fetch-Site` verification; one origin through the reverse proxy, so no production CORS. Data Protection keys live in `infra.data_protection_keys`, encrypted by a certificate present only in the `api` container.

**Reason.** Confirmed: disabling an employee's login must work immediately; CSRF protection is required. Cookie sessions avoid tokens in browser storage and allow immediate kill by rotating the stamp.

**Consequences.** A cookie copied before logout stays valid until expiry or stamp rotation (accepted trade-off; logout rotating the stamp is a one-line option). A restore returns old password hashes, so a mandatory lockdown procedure (ADR-015) exists. The Worker does not authenticate browser sessions and has no access to the key table.

---

## ADR-006: One operational role per account; permissions as data changed only by migration

**Decision.** `identity.user_roles` is unique on `user_id`; permissions are the single role's set (no union). Permission keys follow `<Module>.<Resource>.<Action>[.<Scope>]` and are mapped to roles by reviewed deployment migrations (no in-app role editor in V1). Contact-bearing keys can never map to DOCTOR (migration and startup tests). Keys for unresolved actions (`Orthodontics.Terms.Set`, `Finance.Discount.Set`) exist but are assigned to no role and have **no mapped route**; a mapped route whose key has no role fails the build. Composite policies (`HasAllPermissions`) express "both keys required" (patient registration).

**Reason.** Confirmed: a Doctor account must never gain contact access through another role. A union of roles would make that guarantee depend on every role combination. Open business decisions must not become silently reachable.

**Consequences.** A person needing Doctor and Sales functions needs separate accounts or an explicitly designed role definition. Role changes are Manager actions that rotate the stamp. Two technical Manager-only keys (`Scheduling.Appointments.UpdateStatus`, `Reports.Export`) were added because the confirmed matrix had no actor for appointment status changes and each export has its own key; both are seeds, not clinic rules.

---

## ADR-007: Append-only ledger; Billed, Collected and Outstanding are derived

**Decision.** `patient_charges`, `payments`, `payment_allocations`, `doctor_earnings`, `revenue_distributions`, fixed-lab tables, adjustments, attribution holds and related tables are insert-only (the application role has no UPDATE or DELETE grant). No stored balance exists. A correction is a new row: a negative payment pointing at the original, a negated earning/completion, or a signed adjustment. Money is `numeric(12,2)`, arithmetic in `decimal`, API money as strings. `RecordPayment` is one transaction with a client idempotency key (unique), ordered row locks on charges, and explicit (never automatic) allocation to charges.

**Reason.** Confirmed: financial history is never overwritten; Charge and Payment are separate; Billed/Collected/Outstanding are never conflated; duplicate-payment protection.

**Consequences.** Every figure is a query over immutable rows (exact and reproducible). The refund/correction **workflow** is undefined, so only tested internal primitives exist and no endpoint is mapped (a reversal would flow into the next Doctor payroll as a negative amount, silently deciding clawback). Rounding: half-up for Doctor share and lab; Clinic Share is the remainder so the three parts sum exactly (database CHECK).

---

## ADR-008: Work items and percentage snapshots carry financial ownership

**Decision.** Financial ownership is a property of `clinical.case_work_items` (performing Doctor, price entered, percentage snapshot read once from the Doctor's current percentage), not of the treatment case. A treatment case may have many work items and many charges (one per work item). The charge carries write-once copies of Doctor, case and percentage, protected by composite FKs and an equality test. Continuing a case adds a treatment record only; a new work item is created only when `separatelyPricedWork = true` (refused on ortho cases). Patient–Doctor assignment history is separate and never referenced by historical work.

**Reason.** Confirmed: financial ownership generally follows the Doctor's performed work and percentage snapshot (future collections after reassignment or Doctor deactivation are subject to Q-FIN-9 and may remain Pending Doctor Attribution); patient assignment and financial ownership are distinct; the percentage is snapshotted when the work begins and never recalculated; multiple Doctors may work one case.

**Consequences.** Reassignment cannot move history. The snapshot is taken before any charge exists, so the work item is a lifecycle table frozen by trigger G6. A Doctor who continues another's case without separately priced work gets no financial ownership (the sharing rule is OPEN).

---

## ADR-009: Doctor Share is recognized at collection and is independent of lab; Lab/Clinic completion is separate

**Decision.** For every allocation to a treatment charge, `finance.doctor_earnings` records `doctor_share = round_half_up(allocated × snapshot / 100, 2)` immediately when Doctor attribution is clear, for general-lab and fixed-lab work alike. `finance.revenue_distributions` holds only the Lab and Clinic **completion**, with `CHECK doctor_share + lab_amount + clinic_share = allocated_amount` and a composite FK to the earning. A pending completion is the absence of a row. A general-lab completion is generated only when every candidate lab-rate rule gives the same answer (W2 stays open). Payroll aggregates `doctor_earnings`. Allocations held after a reassignment/deactivation with an outstanding balance receive a pending-attribution row and no earning (Q-FIN-9 stays open). A Fixed Lab revision can never change a recognized Doctor Share.

**Reason.** Confirmed: Collected = Doctor Share + Lab + Clinic, with lab not removed before the Doctor percentage. The user directed that the Doctor Share never wait for the lab rule, and that open questions (Q-FIN-1, W2, Q-FIN-9) stay open with neutral seams.

**Consequences.** Fixed-lab cases feed Doctor payroll from the first collection; Lab/Clinic reporting for them is incomplete until Q-FIN-1 (shown as Pending completion). Clinic Gross Revenue excludes pending amounts; the operating result is shown with notices. Later calculators insert rows under a new `rule_version` without rewriting history.

---

## ADR-010: One fixed lab obligation per case, as revisions

**Decision.** `finance.fixed_lab_expenses` has exactly one row per fixed-lab treatment case (unique `treatment_case_id`, insert trigger G7 requiring `procedure_types.lab_model = 'Fixed'`), and `finance.fixed_lab_revisions` hangs off it (revision 1 = the Doctor's initial entry; a Manager edit inserts the next revision with a reason; every edit audited). No relationship exists between the fixed lab and charges, payments or Doctors, and no snapshot of the amount is copied anywhere.

**Reason.** Confirmed: one fixed lab amount per case, however many Doctors or payments; the Doctor enters it, the Manager may edit it with full audit. Attaching it to charges would have allowed multiple amounts and doubled the lab expense.

**Consequences.** The schema is ready for any Q-FIN-1 rule (inputs are all immutable and queryable). General lab is untouched and stays percentage-based.

---

## ADR-011: Finalized-visit guard and lifecycle tables instead of blanket immutability

**Decision.** Tables are classified insert-only (H), lifecycle with named mutable columns (L), or mutable audited (M). Prescriptions and visit-owned clinical records are editable while their visit is not finalized and are blocked after finalization by one shared trigger function (G1) unless the transaction carries `SET LOCAL clinic.audited_correction = 'on'`, set only by the (not yet built) audited-correction code path, which also writes the audit entry. Payroll and expense tables are frozen by field (G2, G3), allowing status/finalized/paid transitions. Work items are frozen by G6. Column-level `UPDATE` grants protect frozen columns a second time.

**Reason.** The user corrected an over-broad "no updates to prescriptions/payroll" rule: prescription history must be preserved and post-finalization changes auditable, but pre-finalization editing and legitimate status transitions must remain possible; payroll immutability means financial immutability. The post-finalization correction workflow is OPEN (Q-CLN-1).

**Consequences.** Until Q-CLN-1 is answered, nobody (including the Manager) can change a finalized record. The G1 flag guards against accidents, not a compromised application; it is set in one class (architecture-tested).

---

## ADR-012: Hangfire Worker; notifications with dedupe keys and per-item transactions

**Decision.** All time-based work runs only in the Worker (Hangfire, PostgreSQL storage; job arguments are ids only). Planners (J1–J3) live in the owning module and enqueue notifications with `ON CONFLICT (dedupe_key) DO NOTHING`; the key is `{type}:{entity_id}:{clinic-local target date}:{recipient_user_id}`. "Tomorrow" is a clinic-local calendar date computed through one `IClinicClock` (NodaTime, configurable timezone). The dispatcher (J4, every minute) processes each notification in its own transaction with `FOR UPDATE SKIP LOCKED`, commits a failure as `Failed` in a new short transaction, and never rolls back other items; retry (J5, cap 5) reuses the same row and key. The Worker resolves active Sales users and Sales-owner activity only through two minimal views behind Identity/Staff contracts. No Doctor receives notifications.

**Reason.** Confirmed: idempotent, retry-safe, timezone-aware, duplicate-resistant processing; Sales as recipients; no Doctor inbox. Least privilege keeps the Worker away from password hashes, contacts, clinical notes and staff compensation.

**Consequences.** Running a job twice is harmless; one poison notification never blocks others. A planner window of 15 minutes means an item created at 23:55 for "tomorrow" may miss its reminder if the next run is after midnight (accepted). J6 is inert until Q-FDB-1 is answered. J2 remains valid while Q-ORT-2 is open because it checks the ortho charge's Outstanding.

---

## ADR-013: API conventions; unresolved business actions have no route

**Decision.** Audience prefixes with one OpenAPI document each; JSON with unknown members rejected; money as strings; RFC 7807 errors with stable codes and no echoed input; optimistic concurrency via ETag/`If-Match` (`xmin`), `Idempotency-Key` required on payments; search with names or phones is always a POST body; sort/filter whitelists; scoped resources return 404; six anonymous endpoints only. Actions whose actor or workflow is undecided (discounts, ortho terms, refunds, installment application, attribution resolution, post-finalization correction, account-recovery initiation, walk-in owner assignment) have **no mapped route** and no `/api/unassigned/*` placeholder.

**Reason.** Confirmed: concurrency safety and duplicate protection; backend authorization; privacy; the user's rule against inventing workflows and capabilities.

**Consequences.** Some confirmed fields (per-installment paid/status, consultation fees, discounts) cannot be fully populated until their questions are answered; reads show explicit pending markers instead of invented values. The startup test treats a mapped route with an unassigned key as a build failure.

---

## ADR-014: Single-VM deployment with expand/contract migrations and compatibility-based readiness

**Decision.** One Linux VM runs Caddy (TLS, SPA, proxy), `api`, `worker`, plus one-shot `migrator` and `backup` containers; PostgreSQL is managed (container only for Development). CI/CD on GitHub Actions deploys the exact tested image digests (staging automatically, production by approved tag). The application never migrates itself; only the migrator (with `clinic_migrator`) changes the schema, using expand/contract so the previous release keeps working. `/health/ready` checks `required − applied` migrations (extra applied migrations are ignored), so an automatic application rollback against a newer additive schema stays Ready. Non-transactional migrations require an explicit marker, single purpose, retry-safe statements and an invalid-index pre-flight. A CI N-1 compatibility job proves the previous release works while the new migrator runs and after rollback. There is no automatic database downgrade.

**Reason.** Confirmed: Docker, Linux, HTTPS, cloud-hosted, managed PostgreSQL preferred, GitHub Actions, no destructive migration at startup, no Kubernetes. Operable by one person.

**Consequences.** One VM is a single point of failure (recovery by rebuild; data safe in the managed database). Only the immediately previous release is guaranteed rollback-compatible. Secrets are Docker secrets from a root-owned directory; the Data Protection certificate is only in `api`.

---

## ADR-015: Three-layer backups; restricted backup role; encrypted to two independent recipients; mandatory post-restore lockdown

**Decision.** (1) Provider backups with PITR; (2) an independent nightly `pg_dump` by the `backup` container, client-side `age`-encrypted to **two independent recovery recipients** (either private key alone decrypts: redundancy, not two-person approval; custody OPEN and not tied to any application role; no private key on the VM), uploaded with write-only credentials to object-locked storage at a different provider/account, with per-run verification, manifest and ledger integrity checks; (3) a pre-deploy restore point. `clinic_backup` has **explicit SELECT grants only** (no `pg_read_all_data`; no access to `infra.data_protection_keys`, which is also excluded from the dump). After any restore to an earlier point the operator runs `post-restore-lockdown`: rotate all security stamps, set `password_hash = NULL` with reset-required for all staff (narrowed to affected users only when the old audit log is intact), and re-apply deactivations/role changes where evidence is reliable. Restore drills use real data and never run in staging.

**Reason.** Confirmed: automated, encrypted, off-site, retained, restore-tested backups; PITR where supported; least privilege. The audit log never stores passwords or hashes, so a post-restore password cannot be reconstructed; least privilege forbids a backup credential that can read key material.

**Consequences.** A restore signs everyone out and forces password recovery (delivery of reset tokens is the OPEN account-recovery configuration, Q-ACC-1). Loss of both recovery keys makes off-site backups unreadable. RPO/RTO defaults (A: none/4 h, B: 5 min/4 h, C: 24 h/1 day) must be accepted by the Manager and depend on the chosen provider's PITR granularity.
