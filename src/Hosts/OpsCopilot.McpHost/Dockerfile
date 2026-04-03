# syntax=docker/dockerfile:1
# Build context: solution root (g:\ops-copilot-platform\)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# global.json excluded via .dockerignore; sdk:10.0 base image provides the SDK
COPY OpsCopilot.sln ./
COPY src/ src/

RUN dotnet restore src/Hosts/OpsCopilot.McpHost/OpsCopilot.McpHost.csproj

RUN dotnet publish src/Hosts/OpsCopilot.McpHost/OpsCopilot.McpHost.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

# -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# MCP tool server — HTTP/SSE transport on port 8081
EXPOSE 8081
ENV ASPNETCORE_URLS=http://+:8081

COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .
COPY --chown=$APP_UID:$APP_UID packs/ packs/

USER $APP_UID

ENTRYPOINT ["dotnet", "OpsCopilot.McpHost.dll"]
