# syntax=docker/dockerfile:1
#
# Two images are built from this file:
#   api      the web API (the default, last stage)
#   migrate  a one-shot job that applies the EF Core migrations, then exits
#
# Build context: the repository root (the API project references three sibling projects).
#
#   docker build -t vehicle-rental-api .
#   docker build --target migrate -t vehicle-rental-migrate .

ARG DOTNET_VERSION=10.0

# ---- restore: dependencies only, so this layer is reused until a project file changes ----
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS restore
WORKDIR /src

COPY dotnet-tools.json ./
COPY src/VehicleRental.Domain/VehicleRental.Domain.csproj src/VehicleRental.Domain/
COPY src/VehicleRental.Application/VehicleRental.Application.csproj src/VehicleRental.Application/
COPY src/VehicleRental.Infrastructure/VehicleRental.Infrastructure.csproj src/VehicleRental.Infrastructure/
COPY src/VehicleRental.Api/VehicleRental.Api.csproj src/VehicleRental.Api/
RUN dotnet restore src/VehicleRental.Api/VehicleRental.Api.csproj

# Source code comes after restore, so editing code does not repeat the restore.
COPY src/ src/

# ---- publish: the compiled API, without the SDK or source ----
FROM restore AS publish
RUN dotnet publish src/VehicleRental.Api/VehicleRental.Api.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

# ---- bundle: a self-contained executable holding every migration ----
FROM restore AS bundle
ARG TARGETARCH
RUN dotnet tool restore
# A self-contained bundle needs the packages restored for its target runtime (the shared restore above is runtime-neutral).
RUN case "${TARGETARCH}" in arm64) rid=linux-arm64 ;; *) rid=linux-x64 ;; esac \
    && dotnet restore src/VehicleRental.Infrastructure/VehicleRental.Infrastructure.csproj --runtime "${rid}" \
    && dotnet ef migrations bundle \
        --project src/VehicleRental.Infrastructure \
        --startup-project src/VehicleRental.Infrastructure \
        --self-contained --runtime "${rid}" \
        --output /out/efbundle --force

# ---- migrate: applies the schema, then exits. No SDK, no source, no API ----
FROM mcr.microsoft.com/dotnet/runtime-deps:${DOTNET_VERSION} AS migrate
WORKDIR /app
COPY --from=bundle /out/efbundle ./efbundle
# The bundle unpacks itself on first run; give the non-root user a place to do that.
ENV DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/.net
# Runs as the unprivileged "app" user the official image provides.
USER $APP_UID
# The connection string comes from the ConnectionStrings__VehicleRentalDatabase environment variable.
ENTRYPOINT ["./efbundle"]

# ---- api: the runtime image (default target) ----
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS api
WORKDIR /app
COPY --from=publish /app/publish ./

# Plain HTTP on 8080. TLS belongs to whatever sits in front of the container (a reverse proxy or load balancer).
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# The official image ships an unprivileged "app" user; the process does not run as root.
USER $APP_UID

# Exec form, so the .NET host is PID 1 and receives SIGTERM directly for a clean shutdown.
ENTRYPOINT ["dotnet", "VehicleRental.Api.dll"]
