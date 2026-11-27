FROM node:24-bookworm-slim AS web-build
WORKDIR /src

COPY package.json ./
COPY package-lock.json ./
COPY vendor ./vendor
# Prefer the server-supplied SDK build, which carries the Discord theme bridge.
RUN set -eu; \
    node -e "const p = require('./vendor/embedded-app-sdk/package.json'); if (p.name !== '@discord/embedded-app-sdk' || p.version !== '3.0.0-beta.4') throw new Error('Expected the manually supplied @discord/embedded-app-sdk 3.0.0-beta.4 package.');"; \
    grep -q 'createDiscordEnvironment' vendor/embedded-app-sdk/output/Discord.mjs || { echo 'The supplied Embedded App SDK is missing createDiscordEnvironment.' >&2; exit 1; }; \
    grep -q 'DISCORD_ENV_UPDATE' vendor/embedded-app-sdk/output/frame.mjs || { echo 'The supplied Embedded App SDK is missing DISCORD_ENV_UPDATE theme support.' >&2; exit 1; }; \
    grep -q 'applyDiscordEnv' vendor/embedded-app-sdk/output/frame.mjs || { echo 'The supplied Embedded App SDK is missing its Discord theme applier.' >&2; exit 1; }; \
    npm ci

COPY . ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Debug
WORKDIR /src

COPY AITSYS.Discord.LibraryDevelopmentTracking.csproj ./
RUN dotnet restore AITSYS.Discord.LibraryDevelopmentTracking.csproj --no-cache

COPY . ./
COPY --from=web-build /src/wwwroot/app.js ./wwwroot/app.js
COPY --from=web-build /src/wwwroot/app.js.map ./wwwroot/app.js.map
COPY --from=web-build /src/wwwroot/app.css ./wwwroot/app.css
COPY --from=web-build /src/wwwroot/app.css.map ./wwwroot/app.css.map
RUN dotnet publish AITSYS.Discord.LibraryDevelopmentTracking.csproj -c ${BUILD_CONFIGURATION} -o /app/publish /p:UseAppHost=false /p:LangVersion=preview

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt update
RUN apt install -qq --yes wget
RUN groupadd --system appgroup && useradd --system --gid appgroup appuser
WORKDIR /app

ENV DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish ./
RUN chown -R appuser:appgroup /app
USER appuser

ENTRYPOINT ["dotnet", "AITSYS.Discord.LibraryDevelopmentTracking.dll"]
