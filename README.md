# Job Application Tracker .NET

A REST API for managing job applications, follow-up actions, and application status history.

## Prerequisites

- .NET 10 SDK
- Docker Desktop or Docker Engine with Docker Compose

## Local database

Copy `.env.example` to `.env`, set a strong local-only password, then start SQL Server:

```powershell
Copy-Item .env.example .env
# Edit .env and set MSSQL_SA_PASSWORD before continuing.
docker compose up -d --wait sqlserver
```

Stop the container without deleting its data:

```powershell
docker compose down
```

## Build and run

```powershell
dotnet restore
dotnet build
dotnet run --project src/JobApplicationTracker.Api
```
