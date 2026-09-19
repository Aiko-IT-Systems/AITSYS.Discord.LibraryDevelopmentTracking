FROM node:24-bookworm-slim AS web-build
WORKDIR /src

COPY package.json ./
COPY package-lock.json ./
RUN npm ci

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
