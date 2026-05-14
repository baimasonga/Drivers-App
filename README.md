# AVDP SmartFleet Logbook

Digital vehicle movement authorization and GPS verification system for the **Agricultural Value Chain Development Project (AVDP)** in Sierra Leone. Replaces the paper-based vehicle itinerary logbook with a controlled digital workflow integrated with GPS-Trace.

This repository contains:
- **Backend API + web admin dashboard** (`src/AvdpSmartFleet.Api`)
- **Driver mobile app** as a PWA installable on any Android phone (`src/AvdpSmartFleet.DriverApp`)
- **Real GPS-Trace HTTP client** (Wialon API) and a mock for development
- **Background polling service** that detects unauthorized vehicle movement

## Stack

- **.NET 8** ASP.NET Core Web API + Razor Pages
- **Entity Framework Core** with **SQLite** (swappable to PostgreSQL by changing the provider in `Program.cs`)
- **JWT** bearer auth, **BCrypt** password hashing
- **Swagger / OpenAPI** for API exploration
- **Mock GPS-Trace client** — swap with real HTTP client once API credentials are available

## Quick start

**Backend + admin dashboard:**

```powershell
cd src\AvdpSmartFleet.Api
dotnet run --urls "http://localhost:5099"
```

Then open:

- Admin dashboard: <http://localhost:5099/admin>
- Swagger API: <http://localhost:5099/swagger>

**Driver PWA (in a separate terminal):**

```powershell
cd src\AvdpSmartFleet.DriverApp
dotnet run --urls "http://localhost:5100"
```

Open <http://localhost:5100> on a phone (same Wi-Fi) or desktop browser. On Android Chrome use "Add to Home Screen" to install as a PWA.

The SQLite database `avdp_smartfleet.db` is created automatically on first run and seeded with demo users, a vehicle, a GPS unit, and 5 geofences.

## Seeded demo accounts

| Email | Password | Role |
|---|---|---|
| admin@avdp.sl | admin123 | Admin |
| fleet@avdp.sl | fleet123 | Fleet Officer |
| manager@avdp.sl | manager123 | Manager |
| gate@avdp.sl | gate123 | Gate Officer |
| requester@avdp.sl | user123 | Requester |
| driver@avdp.sl | driver123 | Driver |
| auditor@avdp.sl | audit123 | Auditor |

**Change all passwords before deployment.**

## End-to-end happy path (using Swagger)

1. `POST /api/auth/login` as `requester@avdp.sl` — copy the token.
2. Click **Authorize** in Swagger, paste `Bearer <token>`.
3. `POST /api/travel-requests` with a destination geofence ID (e.g. 2 = Kambia District Office) and planned departure/return.
4. Log in as `fleet@avdp.sl`, `POST /api/travel-requests/{id}/assign` with `{ vehicleId: 1, driverId: 1 }`.
5. Log in as `manager@avdp.sl`, `POST /api/travel-requests/{id}/approve` with `{ approve: true }`.
6. Log in as `gate@avdp.sl`, `POST /api/trips/gate-clearance` with `action: 0` (Exit).
7. Log in as `driver@avdp.sl`, `POST /api/trips/log` with `eventType: "Departed"`, then `"Arrived"`, then `"Returning"`.
8. Gate officer: `POST /api/trips/gate-clearance` with `action: 1` (Entry). Reconciliation runs automatically and compliance score is computed.
9. Open `/admin/requests/{id}` to see the trip with logs, gate events, exceptions, and score.

## Project structure

```
src/AvdpSmartFleet.Api/
├── Controllers/      REST API controllers
├── Data/             EF DbContext + seeder
├── Domain/           Entities and enums
├── Dtos/             Request/response shapes
├── Pages/Admin/      Razor Pages admin UI
├── Services/         JWT, audit, reconciliation, GPS-Trace
├── wwwroot/          Static CSS
├── Program.cs
└── appsettings.json
```

## Workflow status machine

```
Draft → Submitted → PendingFleetReview → PendingApproval → ApprovedForDispatch
  → ReadyForGateClearance → TripStarted → InTransit → ArrivedAtDestination
  → Returning → ReturnedToBase → PendingVerification → Verified/Queried/Escalated → Closed
```

Side branches: `Rejected`, `Cancelled`, `Aborted`.

## RBAC summary

| Role | Key permissions |
|---|---|
| Requester | Submit travel requests, cancel own draft/submitted |
| Driver | View own assignments, log trip events |
| FleetOfficer | Assign vehicle/driver, verify trips, manage geofences/vehicles |
| Manager | Final approval of requests |
| GateOfficer | Record vehicle exit/entry |
| Auditor | Read-only access to all reports |
| Admin | Full control, user management |

## GPS-Trace integration

Two implementations of `IGpsTraceClient` ship out of the box:

- `MockGpsTraceClient` — simulates a Freetown HQ → Kambia → back journey, used for development.
- `GpsTraceHttpClient` — real Wialon-style HTTP client used by the live GPS-Trace platform. Calls `token/login`, `core/search_items`, `messages/load_interval` and `messages/get_messages`.

To switch to live data:

```json
"GpsTrace": {
  "UseReal": true,
  "BaseUrl": "https://hosting.gps-trace.com",
  "Token": "<your-application-token>"
}
```

A background `GpsPollingService` polls each registered vehicle every 60 seconds, persists positions, and creates `UnauthorizedMovement` records when a vehicle moves >5 km/h without an active approved trip.

## Reconciliation engine

Implemented in `Services/ReconciliationService.cs`. On gate entry, the engine:

- Fetches GPS-Trace history for the trip window
- Verifies destination geofence entry
- Computes distance vs odometer
- Detects unauthorized stops (stationary >10 min outside any geofence)
- Flags late returns
- Generates a 0–100 compliance score

## Next steps (out of scope for this MVP build)

- Real GPS-Trace HTTP client
- Diversion approval workflow endpoints
- Offline sync support on the mobile side (driver app)
- Background job for unauthorized movement detection
- Reports module (PDF/Excel exports)
- Android driver app (separate project)
- Migration from SQLite to PostgreSQL for production

See `AVDP_SmartFleet_Logbook_Workflow.md` and `AVDP_SmartFleet_Logbook_Addendum.md` (in user Downloads) for the full functional specification, including all 18 edge cases.

## License

Internal AVDP project. All rights reserved.
