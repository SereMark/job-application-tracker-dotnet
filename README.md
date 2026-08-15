# Job Application Tracker .NET

A REST API for managing job applications, follow-up actions, and application status history.

## Prerequisites

- .NET 10 SDK
- Docker Desktop or Docker Engine with Docker Compose

## Run the complete stack

Copy `.env.example` to `.env`, set a strong local-only password, then build and start the API and SQL Server:

```powershell
Copy-Item .env.example .env
# Edit .env and set MSSQL_SA_PASSWORD before continuing.
docker compose up --build
```

The API is available at `http://localhost:8080`, the Scalar API reference at
`http://localhost:8080/scalar/v1`, and the readiness check at
`http://localhost:8080/health/ready`. Example requests are in
`requests/JobApplicationTracker.Api.http`.

Stop the containers without deleting the SQL Server data volume:

```powershell
docker compose down
```

## Run the API directly

Start only SQL Server:

```powershell
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
dotnet run --project src/JobApplicationTracker.Api
```

## Test

Docker must be running because the integration tests start an isolated, temporary SQL Server container. The test container and its databases are removed automatically after the test run.

```powershell
dotnet test
```
