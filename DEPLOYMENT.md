# Trade Titans — MonsterASP.NET Deployment Guide

This document describes how to deploy the Trade Titans ASP.NET Core API to MonsterASP.NET hosting.

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Pre-Deployment Configuration](#pre-deployment-configuration)
3. [Publish the API](#publish-the-api)
4. [MonsterASP.NET Configuration](#monsteraspnet-configuration)
5. [Environment Variables](#environment-variables)
6. [Database Configuration](#database-configuration)
7. [Post-Deployment Testing](#post-deployment-testing)
8. [Angular Frontend Deployment](#angular-frontend-deployment)
9. [Troubleshooting](#troubleshooting)

---

## Prerequisites

- **.NET 10.0 SDK** (or later) installed on your development machine
- **MonsterASP.NET hosting account** with a provisioned ASP.NET Core application
- **Cloudflare Pages account** (for Angular frontend hosting)
- Access to the Trade Titans GitHub repository

---

## Pre-Deployment Configuration

### 1. Update CORS Allowed Origins

Before publishing, update the CORS configuration to include your production Angular domain.

**File:** `src/TradeTitans.Api/appsettings.json`

```json
{
  "Cors": {
    "AllowedOrigins": "https://your-angular-domain.pages.dev"
  }
}
```

Or set via environment variable on MonsterASP (recommended for production):
```
Cors__AllowedOrigins=https://your-angular-domain.pages.dev
```

### 2. Configure Python API URL (if different from default)

The default Python service URL is already configured:
```
https://trade-titan-seven.vercel.app
```

To override via environment variable:
```
PythonService__BaseUrl=https://your-python-service.vercel.app
```

### 3. Alpaca Configuration (PAPER TRADING ONLY)

**IMPORTANT:** The application defaults to `UseMock = true`. This is the safe default.

To enable real Alpaca PAPER trading (NOT live trading), set these environment variables:
```
Alpaca__ApiKey=YOUR_PAPER_API_KEY
Alpaca__SecretKey=YOUR_PAPER_SECRET_KEY
Alpaca__UseMock=false
```

**NEVER set `UseMock=false` without valid PAPER credentials.**
**NEVER use live trading credentials.**

---

## Publish the API

### Step 1: Restore and Build

```bash
cd D:\TeamTitans
dotnet restore
dotnet build -c Release
```

### Step 2: Run Tests

```bash
dotnet test
```

Expected: All tests pass (45+ tests).

### Step 3: Publish

```bash
dotnet publish src/TradeTitans.Api/TradeTitans.Api.csproj -c Release -o ./publish
```

### Published Files

The `./publish` directory will contain:
- `TradeTitans.Api.dll` — Main application
- `TradeTitans.Api.exe` — Executable (Windows)
- `appsettings.json` — Configuration template
- `web.config` — IIS configuration (auto-generated)
- All required DLLs and dependencies

**Deploy the entire `publish` folder contents to MonsterASP.NET.**

---

## MonsterASP.NET Configuration

### Application Settings (via MonsterASP Control Panel)

1. **Log in to MonsterASP.NET Control Panel**
2. **Navigate to your hosting account**
3. **Go to "ASP.NET Core Settings" or "Application Settings"**
4. **Set the following:**

| Setting | Value |
|---------|-------|
| .NET Version | .NET 10.0 |
| Pipeline Mode | Integrated |
| Application Pool | .NET v4.0 (or latest available) |

### Environment Variables (via MonsterASP Control Panel)

Navigate to "Environment Variables" or "App Settings" and add:

| Variable | Value | Required |
|----------|-------|----------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Yes |
| `Cors__AllowedOrigins` | `https://your-angular-domain.pages.dev` | Yes |
| `PythonService__BaseUrl` | `https://trade-titan-seven.vercel.app` | No (has default) |
| `PythonService__TimeoutSeconds` | `120` | No (has default) |
| `Swagger__Enabled` | `true` | No (set to false to disable) |
| `ConnectionStrings__DefaultConnection` | `Data Source=App_Data/tradetitans.db` | Recommended |
| `Alpaca__UseMock` | `true` | Yes (unless using paper credentials) |

**Note:** Use double underscore (`__`) as the section separator in environment variables.

---

## Environment Variables

### Complete Reference

| Variable | Description | Default | Production Value |
|----------|-------------|---------|------------------|
| `ASPNETCORE_ENVIRONMENT` | Runtime environment | `Development` | `Production` |
| `Cors__AllowedOrigins` | Comma-separated allowed CORS origins | `http://localhost:4200,https://localhost:4200` | Your Angular domain |
| `PythonService__BaseUrl` | Python analytics service URL | `https://trade-titan-seven.vercel.app` | Same or custom |
| `PythonService__TimeoutSeconds` | HTTP timeout for Python calls | `120` | `120` |
| `Alpaca__BaseUrl` | Alpaca API endpoint | `https://paper-api.alpaca.markets` | Same |
| `Alpaca__ApiKey` | Alpaca API key | `` (empty) | Your PAPER key |
| `Alpaca__SecretKey` | Alpaca secret key | `` (empty) | Your PAPER secret |
| `Alpaca__UseMock` | Enable mock mode | `true` | `true` (or `false` with paper creds) |
| `ConnectionStrings__DefaultConnection` | SQLite connection string | `Data Source=tradetitans.db` | `Data Source=App_Data/tradetitans.db` |
| `Swagger__Enabled` | Enable Swagger UI | `true` | `true` (for testing) |

---

## Database Configuration

### SQLite on MonsterASP.NET

The application uses SQLite for the audit trail database. On MonsterASP.NET:

1. **Database Location:** Use the `App_Data` folder for persistent storage:
   ```
   ConnectionStrings__DefaultConnection=Data Source=App_Data/tradetitans.db
   ```

2. **Persistence:** The `App_Data` folder is the standard location for application data on IIS/MonsterASP.NET and is typically persisted across deployments.

3. **Initialization:** The database is automatically created on first run if it does not exist.

4. **Migrations:** Schema migrations are applied automatically on startup (idempotent).

### Important Notes

- **Do NOT store the database in the application root** — it may be overwritten on deployment.
- **Back up the database** before major deployments.
- The database file (`tradetitans.db`) and its companion files (`-shm`, `-wal`) must all be in the same directory.

---

## Post-Deployment Testing

### 1. Health Check

```bash
curl https://your-monsterasp-domain/health
```

Expected response:
```json
{
  "status": "ok",
  "timestamp": "2026-09-04T00:00:00Z",
  "environment": "TradeTitans.Api"
}
```

### 2. Swagger UI

Navigate to:
```
https://your-monsterasp-domain/swagger
```

Verify all endpoints are visible:
- `GET /api/commandcenter/health`
- `GET /api/commandcenter/snapshot/{symbol}`
- `GET /api/commandcenter/options/{symbol}`
- `POST /api/council/run/{symbol}`
- `POST /api/council/sessions/{id}/confirm`
- `POST /api/council/sessions/{id}/cancel`
- `GET /api/council/sessions`
- `GET /api/council/sessions/{id}`
- `GET /api/portfolio/account`
- `GET /api/portfolio/positions`
- `GET /api/portfolio/orders`
- `GET /api/riskguardian/active-rules`
- `GET /api/riskguardian/veto-logs`

### 3. Test Symbol Lookup

```bash
curl https://your-monsterasp-domain/api/council/run/AAPL
```

Expected: Returns council debate result with market data.

### 4. Test Invalid Symbol

```bash
curl https://your-monsterasp-domain/api/council/run/INVALIDXYZ
```

Expected: HTTP 422 with error code `SYMBOL_UNAVAILABLE`.

### 5. Test NO_TRADE Verdict

```bash
curl https://your-monsterasp-domain/api/council/run/BTC
```

Expected: Returns result with `NO_TRADE` status (no execution).

---

## Angular Frontend Deployment

### Update Production API URL

**File:** `ui/src/environments/environment.prod.ts`

```typescript
export const environment = {
  production: true,
  apiBaseUrl: 'https://your-monsterasp-domain/api',
  appName: 'Trade Titans'
};
```

### Build for Production

```bash
cd ui
npm run build
```

### Deploy to Cloudflare Pages

1. **Connect your GitHub repository to Cloudflare Pages**
2. **Set build configuration:**
   - Build command: `npm run build`
   - Build output directory: `dist/ui/browser`
3. **Deploy**

### Verify CORS

After both deployments are live:
1. Open browser developer tools
2. Navigate to your Angular app
3. Check that API calls succeed without CORS errors

---

## Troubleshooting

### Issue: 500 Internal Server Error

**Cause:** Database path not writable or missing dependencies.

**Solution:**
- Verify `App_Data` folder exists and is writable
- Check MonsterASP.NET logs for detailed error
- Ensure .NET 10 runtime is available

### Issue: CORS Errors in Browser

**Cause:** Angular domain not in allowed origins.

**Solution:**
- Verify `Cors__AllowedOrigins` includes your exact Angular domain
- Include protocol (`https://`) and no trailing slash
- Restart the application after changing environment variables

### Issue: Python Service Unreachable

**Cause:** Network issue or Python service down.

**Solution:**
- Verify Python service is accessible: `curl https://trade-titan-seven.vercel.app/health`
- Check firewall rules on MonsterASP.NET
- Increase timeout: `PythonService__TimeoutSeconds=180`

### Issue: Swagger Not Accessible

**Cause:** Swagger disabled in production.

**Solution:**
- Set `Swagger__Enabled=true` in environment variables
- Or access via: `https://your-domain/swagger/index.html`

---

## Security Checklist

- [ ] `ASPNETCORE_ENVIRONMENT` set to `Production`
- [ ] CORS origins restricted to actual production domains
- [ ] `Alpaca__UseMock=true` (unless paper credentials configured)
- [ ] No secrets in `appsettings.json` (all via environment variables)
- [ ] Swagger disabled or protected in production (optional)
- [ ] HTTPS enforced (MonsterASP.NET provides this by default)
- [ ] Database in `App_Data` (not publicly accessible)

---

## Support

For MonsterASP.NET specific issues, consult:
- MonsterASP.NET documentation: https://www.monsterasp.net/help
- ASP.NET Core hosting docs: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/