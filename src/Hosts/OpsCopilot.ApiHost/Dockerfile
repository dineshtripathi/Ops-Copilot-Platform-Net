# syntax=docker/dockerfile:1
# Build context: solution root (g:\ops-copilot-platform\)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy restore targets first for layer-cache efficiency
COPY global.json OpsCopilot.sln ./
COPY src/ src/

# Restore only this project's dependency graph (tests not needed)
RUN dotnet restore src/Hosts/OpsCopilot.ApiHost/OpsCopilot.ApiHost.csproj

RUN dotnet publish src/Hosts/OpsCopilot.ApiHost/OpsCopilot.ApiHost.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

# -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Publish output — owned by the built-in non-root app user ($APP_UID=1654)
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .

# Packs are discovered at runtime via Packs__RootPath (default: "packs")
COPY --chown=$APP_UID:$APP_UID packs/ packs/

USER $APP_UID

ENTRYPOINT ["dotnet", "OpsCopilot.ApiHost.dll"]
