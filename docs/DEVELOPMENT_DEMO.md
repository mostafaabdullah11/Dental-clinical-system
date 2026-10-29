# Development demo data and accounts

**Audience:** developers and reviewers on a local machine. **Never for production, staging or any shared server.**

This is implementation-phase documentation. It does not change the frozen V1.1 baseline: no business rule, role, permission,
status or formula is added, and the demo tool uses only the public API of the application.

## What it is

`tools/Clinic.DemoSeed` is a development-only console tool. It hosts the real API in-process and plays a scripted clinic history
(about two months, ending today) through the real HTTP endpoints with a controllable clock. Every business rule, validation,
audit entry and ledger invariant therefore applies to the demo data exactly as it would to real usage. Nothing is inserted with
SQL, and there are no mock arrays in the frontend.

It is **not** part of any container image (`deploy/docker/*` copy `src/` only), not referenced by any `src` project, and it
refuses to run anywhere that is not a local Development database.

## Safety rules (enforced in code: `DemoGuard`)

| Rule | Behaviour |
|---|---|
| Environment | `ASPNETCORE_ENVIRONMENT` / `DOTNET_ENVIRONMENT` set to anything other than `Development` (for example `Production`, `Staging`) → refused. |
| Server | The database host must be this machine (`localhost`, `127.0.0.1`, `::1`) → otherwise refused. |
| Reset | `reset` only touches a database named `clinic_dev`, `clinic_demo` or `clinic_demo_<suffix>`. |
| Credentials | No production secret exists in the tool. The demo password is a development value (below) that you can override. |
| Roles | Database roles are created only when none of the four exist on the server; existing roles and their passwords are never changed. |
| Idempotent | A finished run marks the clinic name with `(بيانات تجريبية)`; a second run does nothing. A half-finished run is detected (`(قيد تجهيز البيانات التجريبية)`) and must be rebuilt with `reset`. |
| Existing data | A database that already holds staff but no demo marker is refused unless `--allow-existing-data` is passed. |

## Commands

Prerequisites: .NET 10 SDK, a **local** PostgreSQL 18 server, and an administrative connection for the first setup.

```powershell
# Build once (also builds the API it hosts)
dotnet build tools/Clinic.DemoSeed/Clinic.DemoSeed.csproj

# 1) Create (or recreate) a throwaway database, apply every migration, play the demo history.
#    The admin connection is used only for this step. Put the password in PGPASSWORD / pgpass; do not commit it.
$env:CLINIC_DEMO_PG_ADMIN = "Host=localhost;Port=5432;Username=postgres"
dotnet run --no-build --project tools/Clinic.DemoSeed -- reset --database clinic_demo

# 2) Re-run against an existing, empty (migrated) development database. Safe to repeat: it does nothing the second time.
$env:ConnectionStrings__Clinic = "Host=localhost;Port=5432;Database=clinic_dev;Username=clinic_app;Password=<your dev password>"
dotnet run --no-build --project tools/Clinic.DemoSeed -- seed
```

* The history is anchored to the day you run it ("today" has arrivals, a started Visit, the whole queue). After a few days it
  ages: run `reset` again to re-anchor it.
* The tool prints the number of scripted steps, the counts per scenario, and finishes with the ledger integrity check (the same
  independent checks the nightly backup runs); any violation fails the run.
* If a `manager` account already exists with another password, `seed` stops. Pass `--reset-manager-password` to set the demo
  password on the `manager` account (development databases only).

### Run the application against the demo database

```powershell
$env:ConnectionStrings__Clinic = "Host=localhost;Port=5432;Database=clinic_demo;Username=postgres;Options=-c role=clinic_app"  # + PGPASSWORD / pgpass
dotnet run --project src/Clinic.Api      # https://localhost:5081 (development certificate)
cd src/web; npm install; npm run dev     # http://localhost:5173 proxies /api to the API
```

Using the administrative login with `Options=-c role=clinic_app` makes the API run with the application role's limited
privileges, the same technique the integration tests use.

## Accounts

All demo accounts share one **development-only** password, `DemoClinic@2026`, unless you set `CLINIC_DEMO_PASSWORD` before
running the tool. It exists only in this document and in the tool's defaults; the production Compose files, the API and the
web bundle never contain it. Authentication stays username + password.

| Username | Role | Linked record |
|---|---|---|
| `manager` | Manager | the initial Manager (created through the API's own `create-initial-manager` operator command) |
| `doctor.demo` | Doctor | د. كريم فؤاد — general practitioner (the fullest schedule) |
| `sales.demo` | Sales / Reception | ريم علاء |
| `dr.hisham` | Doctor | د. هشام عبد الرحمن — oral surgeon |
| `dr.mona` | Doctor | د. منى السيد — orthodontist |
| `dr.sara`, `dr.ahmed`, `dr.yasmin`, `dr.nourhan` | Doctor | general practitioners |
| `dr.amr` | Doctor (**inactive** at the end of the history) | general practitioner — old work and entitlement stay with them |
| `sales.mohamed` | Sales / Reception | محمد سمير |
| `sales.heba` | Sales / Reception (**inactive** at the end of the history) | هبة جمال |

Assistants (4) exist as staff records only: no account, no login, no role, no dashboard.

In `npm run dev` the login page shows three buttons that fill the demo usernames. To fill the password too, create the untracked
file `src/web/.env.development.local` containing `VITE_DEMO_PASSWORD=<the demo password>`. These buttons are removed from the
production bundle (`import.meta.env.DEV` is false in `vite build`).

## How it is verified

* `tests/Clinic.IntegrationTests/Demo/DemoSeedTests.cs` runs one complete seed on a throwaway database and then checks: a second run
  changes nothing; the staff structure (8 / 3 / 4); the three accounts sign in and are linked to real staff; all seven appointment
  statuses, four follow-up statuses, eight lead statuses and three feedback states are present; patient-origin rules (Walk-In has no
  Original Sales); every ledger invariant (the nightly backup's integrity checks) holds; Lab / Orthodontic examples work through the
  API; Doctor and Sales privacy on the demo data; the dashboards (exactly the eight Sales items, no Doctor dashboard route); and the
  guard refuses Production, Staging, remote servers and non-throwaway reset targets.
* `tests/Clinic.ArchitectureTests/DemoUpgradeContractTests.cs` proves that nothing under `src/` or `deploy/` references the tool or
  contains the demo password, that the Doctor start page calls only the existing own-data endpoints, that the Sales home has exactly
  the eight frozen items, and that the production web bundle contains no demo helper.

## What the dataset covers (all through existing flows)

* **Staff:** 8 Doctors (1 oral surgeon, 1 orthodontist, 6 general practitioners), 3 Sales / Reception, 4 Assistants; effective-dated
  rate changes (lab percentage, one Sales commission, one Doctor percentage); published availability for two weeks.
* **Patients:** 48 synthetic patients (fictional `0100000xxxx` phones, `@example.com` emails, every CRM note carries
  «بيانات تجريبية»). 38 Walk-In (Original Sales = none, zero commission) and 10 converted from Leads (Original Sales = the lead owner).
  Structured Medical History intake for 30 of them; none for the rest (booking never depends on it). About 33 patients have a
  chief complaint recorded by their Doctor (clinical profile, never visible to Sales).
* **Leads:** all eight statuses. **Follow-ups:** all four statuses, all six types, some overdue.
* **Appointments:** all seven statuses (Scheduled, Confirmed, InTreatment, Completed, Cancelled, NoShow, Rescheduled); patients who
  arrived and are waiting; a started and an unfinished Visit; overlapping bookings for one Doctor; yesterday's and tomorrow's days.
* **Clinical:** consultation-only visits; fillings, root canals (canal measurements, steps), extractions, cleaning, bleaching,
  pediatric procedures, implants, prosthetics; prescriptions; Doctor referrals; implant follow-up appointments.
* **Finance:** unpaid, partially paid, fully paid, multiple payments, Doctor- and Sales-set discounts, Patient Credit from an
  overpayment and from an advance payment, credit applied, a Manager refund, a payment reversal, a discount correction, Fixed Lab
  with Lab-first priority and a pending Doctor entitlement, a Fixed Lab revision.
* **Sales commission (Q-COMM-1):** a commission rate is effective from the *database clock* at the moment it is saved and is never
  back-dated, and the replayed history uses a movable application clock. Collections recorded earlier in the replayed history
  therefore predate the saved rates and carry no commission (Walk-In patients never do). To show real figures, the tool also records
  collections on Lead-sourced patients' open balances a few minutes after the rates were saved and drafts this month's Sales payroll
  for two employees, so the commission items, the Sales performance report and the payroll sheet are produced by the real rules.
* **Orthodontics:** three cases (progressed and paid, partly paid with an overdue installment, brand new) and a Manager Gross
  Total increase that adds a PriceChange installment.
* **Feedback:** Pending, Sent and Submitted (patient link and Sales-entered). No expiry exists.
* **Back office:** expenses in all 11 categories (some recorded by Sales in the two allowed ones, one audited edit), attendance for
  Assistants and Sales, approved Assistant overtime, and last month's payroll for Sales, Doctors and Assistants (some Draft, some
  Finalized, some Paid).

The Manager's home page, the Sales dashboard (exactly the eight frozen items) and the Doctor start page are all driven by this data
through the normal API; the Doctor start page uses only the Doctor's own existing endpoints (there is no Doctor dashboard in V1).
