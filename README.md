<p align="center">
  <img src="fiskoda.dashboard/public/fiskodo/logo_fiskodo.png" alt="Fiskodo" width="120" />
</p>

# Fiskodo

A Discord music bot with a web dashboard. Play, pause, skip, and manage queues from Discord or the browser.

**Stack:** .NET 8 API + NetCord + Lavalink · React + Vite dashboard

## Quick Start

**Backend**
```bash
cd fiskodo.backend
# Add your Discord token, Lavalink URL, and JWT secret to appsettings.Local.json
dotnet run
```

**Dashboard**
```bash
cd fiskoda.dashboard
npm install && npm run dev
```

## Project Structure

| Folder | Description |
|--------|-------------|
| `fiskodo.backend` | ASP.NET Core API + Discord bot (NetCord, Lavalink4NET) |
| `fiskoda.dashboard` | React SPA for monitoring and control |

Put secrets in `appsettings.Local.json` (gitignored). See `appsettings.json` for the config template.
