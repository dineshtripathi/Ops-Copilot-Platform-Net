# syntax=docker/dockerfile:1
# Build context: solution root (g:\ops-copilot-platform\)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# global.json excluded via .dockerignore; sdk:10.0 base image provides the SDK
COPY OpsCopilot.sln ./
COPY src/ src/

RUN dotnet restore src/Hosts/OpsCopilot.WorkerHost/OpsCopilot.WorkerHost.csproj

RUN dotnet publish src/Hosts/OpsCopilot.WorkerHost/OpsCopilot.WorkerHost.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

# -------------------------------------------------------
# Use aspnet runtime (Worker SDK apps may reference ASP.NET libs at runtime)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# No EXPOSE — WorkerHost is ingress-disabled in Container Apps
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .
COPY --chown=$APP_UID:$APP_UID packs/ packs/

USER $APP_UID

ENTRYPOINT ["dotnet", "OpsCopilot.WorkerHost.dll"]
