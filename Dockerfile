# syntax=docker/dockerfile:1

# TechieDesk — self-host image (REQ-FN-017/018/019).
# Multi-stage: restore + publish on the .NET 10 SDK, then a slim ASP.NET runtime.
# The app runs its own DbUp migration on start (Wave 0) and blocks serving on failure,
# so `docker compose up` is a one-command boot including schema migration.

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# REQ-NFR-008 (data locality): opt out of the .NET CLI's first-run telemetry ping so that
# building the image performs no outbound call other than the NuGet restore.
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# Copy only the project files first so `restore` is cached across source-only changes.
COPY src/TechieRag/TechieRag.csproj src/TechieRag/
COPY src/TechieRag.Embedded/TechieRag.Embedded.csproj src/TechieRag.Embedded/
COPY apps/TechieDeskDb/TechieDeskDb.csproj apps/TechieDeskDb/
COPY apps/TechieDesk/TechieDesk.csproj apps/TechieDesk/
RUN dotnet restore apps/TechieDesk/TechieDesk.csproj

# Copy the rest of the sources needed to publish the web app.
COPY src/ src/
COPY apps/TechieDeskDb/ apps/TechieDeskDb/
COPY apps/TechieDesk/ apps/TechieDesk/

RUN dotnet publish apps/TechieDesk/TechieDesk.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# 12-factor (REQ-FN-018): everything is overridable via environment variables at run time.
# Kestrel listens on plain HTTP inside the container; TLS is terminated by the edge/proxy.
# REQ-NFR-008: DOTNET_CLI_TELEMETRY_OPTOUT keeps the runtime image from emitting any
# Microsoft telemetry. The instance makes no outbound call except LLM/embedding providers
# and AppManager — both operator-configured.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

COPY --from=build /app/publish .

# Upgrade-safe state (REQ-FN-019): these directories are mounted as named volumes by compose so
# the App DB + RAG store, uploaded documents, and the ~2.3GB embedded BGE-M3 model survive
# container recreation. Created here so the app can write even without an explicit mount.
RUN mkdir -p /app/data /app/uploads /app/models /app/logs

EXPOSE 8080

# The app applies migrations on start (DbUp) before serving; a non-zero migration aborts boot.
ENTRYPOINT ["dotnet", "TechieDesk.dll"]
