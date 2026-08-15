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

Store the API connection string with the same password outside the repository:

```powershell
dotnet user-secrets set --project src/JobApplicationTracker.Api `
  "ConnectionStrings:Database" `
  "Server=127.0.0.1,1433;Database=JobApplicationTracker;User ID=sa;Password=<same-password>;Encrypt=True;TrustServerCertificate=True"
dotnet tool restore
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet ef database update --project src/JobApplicationTracker.Api
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

## Test

Docker must be running because the integration tests start an isolated, temporary SQL Server container. The test container and its databases are removed automatically after the test run.

```powershell
dotnet test
```
