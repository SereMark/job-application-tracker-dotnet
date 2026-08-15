ARG DOTNET_SDK_VERSION=10.0.400
ARG DOTNET_RUNTIME_VERSION=10.0.11

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_VERSION} AS build
WORKDIR /source

COPY .editorconfig Directory.Build.props Directory.Packages.props global.json ./
COPY src/JobApplicationTracker.Api/JobApplicationTracker.Api.csproj \
    src/JobApplicationTracker.Api/
RUN dotnet restore src/JobApplicationTracker.Api/JobApplicationTracker.Api.csproj

COPY src/JobApplicationTracker.Api/ src/JobApplicationTracker.Api/
RUN dotnet publish src/JobApplicationTracker.Api/JobApplicationTracker.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_VERSION} AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER app
COPY --from=build --chown=app:app /app/publish/ ./

ENTRYPOINT ["dotnet", "JobApplicationTracker.Api.dll"]
