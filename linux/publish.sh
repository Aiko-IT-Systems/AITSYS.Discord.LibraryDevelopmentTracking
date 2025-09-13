#!/bin/bash
dotnet publish -c Release -r linux-x64 --self-contained=true -p:PublishSingleFile=false -p:GenerateRuntimeConfigurationFiles=true -o /opt/bots/discord-lib-dev-tracking src/AITSYS.Discord.LibraryDevelopmentTracking.csproj
