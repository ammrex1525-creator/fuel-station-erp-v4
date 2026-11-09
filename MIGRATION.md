# Version 3 to Version 4 migration notes

Source inspected: `C:\Users\ammar\FuelStationERP_V2\Version3`. Do not edit this source during Version 4 work.

| Version 3 source | Version 4 target | Migration notes |
|---|---|---|
| `users` (username, password, role) | ASP.NET Identity users/roles | Never copy plaintext password values. Import username and role mapping; force password reset or invite users. |
| branch access and roles | `UserBranchMembership` plus Identity roles | Import least-privilege branch memberships. Only explicitly assigned global owners receive cross-branch API access. |
| tanks (product, capacity, current and desktop stock/dip fields) | `Branch`, `Tank`, `FuelProduct`, `TankDip` | One tank must belong to a branch and product. Preserve legacy tank id and dated stock snapshots; do not treat cached stock as ledger truth. |
| pumps (pump_number, tank_id) | `Pump` | Resolve tank and branch; verify historical pump assignments before importing sessions. |
| attendants | `StaffAttendant` and optional Identity account | Staff identity and login identity are separate concepts. Salary/payday values require payroll policy. |
| pump_session_audit | `PumpSessionAudit` | Preserve who changed meter readings, when, the reason and before/after values. |
| fuel_receipts | `FuelReceipt` | Keep invoice and received litres separately, remaining batch quantity, supplier snapshot, costs, short/over and receipt type. Effective cost defaults to unit cost; station-borne shortage costs must be applied before historical FIFO is rebuilt. |
| pump_product_runs, pump_run_receipts | `PumpRun`, `PumpRunReceipt`, `FuelReceipt` | Preserve open/closed status, meter snapshots and batch links. |
| pump_sessions | `PumpSession` | Preserve opening/closing meter, litres, price, sales, cash, POS, expenses, RTT, short/over, session date, COGS and margin. Map run and attendant references. |
| fifo_session_allocations | `FifoAllocation` | Preserve batch allocation sequence. Validate totals against session litres and historical receipt remaining quantities. |
| tank_dips | `TankDip` | Preserve actual reading, recorded time, user and expected/variance values where present. |
| expenses | `Expense` | Preserve description, amount, date, source account, linked account transaction and submitter; do not count linked cash outflow twice. |
| accounts | `Account` | Preserve account name, CASH/BANK/POS type, number, provider and active state; names are unique within branch and type. |
| account_transactions | `AccountTransaction`, `CashCollection` | Preserve source/target account, receipt and short-reconciliation links, type, amount, reference and description. Import transaction direction/type without sign normalization. |
| legacy `rtt` table | Pump session RTT fields, or an explicit legacy adjustment | Source comments say current RTT belongs on pump sessions and is measured in litres with FIFO cost. Do not convert legacy amount rows without review. |
| attendant_short_reconciliations and allocations | `AttendantShortReconciliation`, `AttendantShortAllocation` | Keep recovery allocations to original sessions for audit. |
| receipt_short_reconciliations | `ReceiptShortSettlement` | Keep supplier/driver party, disputed state, reconciled and station-borne litres/cost. Include station-borne cost in the correct receipt cost layer before FIFO rebuild. |
| dashboard/report calculations (`dashboard.py`, `report_data.py`, `weekly_reports.py`) | API query/report services (planned) | Recompute from canonical ledger data; compare report totals to Version 3 before cutover. |
| website API, publishing and entry processor | API endpoints + `SyncSubmission` | Replace shared publish token with per-client/user auth, branch authorization, idempotency keys and reviewable submission status. |

## Import sequence

1. Snapshot and checksum the source SQLite database; import into a staging database only.
2. Import branches, products, users/roles, attendants, suppliers, tanks and pumps, recording every source primary key as a legacy key.
3. Import receipts and receipt settlements, then pump runs and run-receipt associations.
4. Import sessions, FIFO allocations, dips, expenses, account transactions, collections and reconciliation allocations.
5. Compare per-day/product litres, stock, FIFO COGS, gross profit, RTT and short/over against Version 3 reports. Record documented opening balances instead of fabricating missing history.
6. Rehearse rollback and perform a supervised parallel run before any production cutover.

No data has been copied or transformed by this scaffold. A direct production migration is deferred until the complete evolved schema and business policies are signed off.

The current API pump-session posting implements the observed source rules: gross meter movement is closing minus opening; RTT cannot exceed that movement; net sold litres equal gross movement minus RTT; sales equal net litres times unit price; short/over equals cash plus POS minus (sales less session expenses); FIFO COGS excludes RTT and uses dated receipt batches ordered by receipt date then ID. It rejects insufficient costed stock and persists the session, batch balances and allocations atomically. Verify rounding and local date conventions against Version 3 reports before production use.

## Version 4 API-first update

The active Version 4 solution includes `FuelStationERP.API`, `FuelStationERP.Web`, `FuelStationERP.Desktop` and `FuelStationERP.Mobile`, with shared `Domain` and `Infrastructure` projects. The API provides JWT/Identity authentication, branch and product endpoints, branch-scoped tanks, pumps, pump runs, attendants, suppliers, fuel receipts, expenses, tank dips, metered pump sessions, non-metered LPG fills and offline submissions. Web and mobile clients call these API operations. The desktop can use the same SQL Server database when configured, while retaining its local SQLite mode. API operations reuse domain calculations and FIFO inventory services rather than duplicating financial rules in each API client. No Version 3 data was copied. The API creates a new Main Branch and PMS, AGO and LPG catalog; operators enter actual tanks, pumps, attendants, costed opening deliveries and transactions.

The desktop workflows use the observed Version 3 meter, RTT, short/over and FIFO rules. Fuel receipts include invoice and received quantity, supplier and delivery details. LPG is a separate non-metered product sale by kilogram, with optional cylinder count and size, and is costed against the selected LPG tank's oldest eligible delivery batches. LPG stock checks use the recorded physical kg quantity. Cylinder deposits, returns, tare-weight rules, safety/certification records and full cylinder asset tracking require a station policy and are not inferred from the Version 3 litre-meter workflows.

Local SQLite path: `%LOCALAPPDATA%\\FuelStationERP\\station-v4.db`. Configure `ConnectionStrings:Erp` on the API for the shared SQL Server deployment; the desktop can use the same connection string. API secrets are supplied through environment variables or a secret store. The first OWNER account can be bootstrapped from `Bootstrap:Username`, `Bootstrap:Email` and `Bootstrap:Password`; subsequent user creation is owner-only. Back up the selected database independently. The current WPF client connects directly to the database rather than the API, while the Web and MAUI clients use the API. SQLite is for one-computer development, not concurrent multi-computer operation. iOS builds need a paired Mac; Android builds need the Google Android SDK.
