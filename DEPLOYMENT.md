# AVDP SmartFleet — Deployment Runbook

This guide walks through a clean production deployment using Docker Compose with automatic HTTPS via Caddy.

## Prerequisites

- Linux server (Ubuntu 22.04 LTS recommended) with at least 2 vCPU, 4 GB RAM, 40 GB disk
- A public IP address (cloud or on-premise)
- Two DNS A/AAAA records pointing at the server:
  - `avdp.example.org` → API + admin dashboard
  - `driver.avdp.example.org` → driver PWA
- Docker and Docker Compose v2 installed
- Ports 80 and 443 open in the firewall

> Replace `avdp.example.org` with the actual AVDP-owned domain everywhere below.

## One-time setup

### 1. Get the code on the server

```bash
git clone <repo-url> /opt/avdp-smartfleet
cd /opt/avdp-smartfleet
```

### 2. Configure environment

```bash
cp .env.example .env
# Generate a strong JWT signing key:
echo "JWT_KEY=$(openssl rand -base64 48)" >> .env
nano .env   # set DRIVER_APP_URL and (when ready) GPS_TRACE_TOKEN
```

### 3. Point Caddy at your real domains

Edit `deploy/Caddyfile`:

- Replace both `avdp.example.org` and `driver.avdp.example.org` with the real hostnames
- Set the `email` directive to a real ops mailbox (Let's Encrypt expiry warnings)

### 4. Set the driver PWA's API URL

Edit `src/AvdpSmartFleet.DriverApp/wwwroot/appsettings.Production.json`:

```json
{ "ApiBaseUrl": "https://avdp.example.org/" }
```

### 5. First build and start

```bash
docker compose up -d --build
```

Caddy will request Let's Encrypt certificates on first start. Watch logs to confirm:

```bash
docker compose logs -f caddy
```

You should see lines like `certificate obtained successfully`. If you see `connection refused`, your DNS isn't pointing at this server yet — wait for propagation.

### 6. Smoke-test

```bash
curl -fs https://avdp.example.org/healthz   # should return Healthy
curl -fs https://avdp.example.org/admin     # should return HTML
curl -fs https://driver.avdp.example.org/   # should return HTML
```

### 7. Change seeded passwords

Log in to <https://avdp.example.org/admin> as `admin@avdp.sl` (password `admin123`). Go to **Users → Add user** and create real accounts. Then disable or change the password on every seeded account.

> **The seeded passwords are public knowledge from the README and must not survive into pilot.**

## GPS-Trace integration

Once AVDP provides the GPS-Trace API token, update `.env`:

```
GPS_TRACE_USE_REAL=true
GPS_TRACE_TOKEN=<token-from-avdp>
```

Then map each AVDP vehicle to its GPS-Trace unit:

```bash
# Find the vehicle in admin, edit it via Swagger:
# POST /api/gps-units  with the External Unit ID from GPS-Trace
# Then update the vehicle's GpsUnitId
```

Restart the API container:

```bash
docker compose restart api
```

The background polling service will start pulling real positions every 60 seconds.

## Day-2 operations

### Updating the application

```bash
cd /opt/avdp-smartfleet
git pull
docker compose up -d --build
```

Database migrations run automatically on container start.

### Backups

The SQLite database lives in the `avdp-data` volume.

```bash
# Backup
docker run --rm -v avdp-smartfleet_avdp-data:/data -v $(pwd):/backup alpine \
    tar czf /backup/avdp-data-$(date +%F).tar.gz -C /data .

# Restore
docker compose down
docker run --rm -v avdp-smartfleet_avdp-data:/data -v $(pwd):/backup alpine \
    tar xzf /backup/avdp-data-YYYY-MM-DD.tar.gz -C /data
docker compose up -d
```

Schedule daily backups via cron. Test restore quarterly.

### Logs

Structured logs roll daily inside the API container:

```bash
docker compose exec api ls /app/logs
docker compose logs -f api          # live tail
```

Logs are retained for 30 days in the `avdp-logs` volume.

### Scaling considerations

SQLite is fine for a single-server pilot up to ~50 vehicles. For larger rollouts, migrate to PostgreSQL:

1. Swap `UseSqlite` for `UseNpgsql` in `Program.cs` (add `Npgsql.EntityFrameworkCore.PostgreSQL` package)
2. Run `dotnet ef migrations add InitialPg --project src/AvdpSmartFleet.Api`
3. Add a `postgres` service to `docker-compose.yml` and update the connection string

## Security checklist before go-live

- [ ] `.env` contains a `JWT_KEY` generated with `openssl rand -base64 48` (or equivalent)
- [ ] Both domains have valid TLS certificates (check `https://...` loads without warning)
- [ ] All seeded demo accounts are either disabled or have rotated passwords
- [ ] At least one real Admin and one real Auditor account exist
- [ ] `DRIVER_APP_URL` in `.env` matches the driver PWA's actual domain (CORS allowlist)
- [ ] Firewall blocks all inbound traffic except 22/80/443
- [ ] Daily off-server backup job is scheduled
- [ ] AVDP has authorised access to GPS-Trace API token storage and rotation
- [ ] Driver privacy notice has been approved by AVDP and is shown on first login
- [ ] Pilot plan signed off with named driver(s), vehicle(s), and review checkpoints

## Rollback

If a deployment breaks something:

```bash
git log --oneline -10                # find the previous good commit
git checkout <previous-good-sha>
docker compose up -d --build
```

If a database migration corrupted state, restore the most recent backup using the procedure above.

## Support contacts

| Role | Contact |
|---|---|
| Application owner | TBD |
| Hosting / Ops | TBD |
| GPS-Trace account holder | TBD |
| AVDP data protection lead | TBD |

Fill these in before pilot. Anyone touching production should know who to call.
