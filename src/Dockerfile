FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS restore
WORKDIR /src
COPY ModeusSchedule.MSAuth.csproj .
RUN dotnet restore "ModeusSchedule.MSAuth.csproj" && \
    set -- /root/.nuget/packages/microsoft.playwright/*/.playwright && \
    test "$#" -eq 1 && \
    mkdir -p /opt/playwright/node && \
    cp -a "$1/package" /opt/playwright/ && \
    cp -a "$1/node/linux-x64" /opt/playwright/node/ && \
    cp -a "$1/node/LICENSE" /opt/playwright/node/ && \
    chmod +x /opt/playwright/node/linux-x64/node

FROM restore AS build
ARG BUILD_CONFIGURATION=Release
COPY . .
RUN dotnet publish "ModeusSchedule.MSAuth.csproj" -c "$BUILD_CONFIGURATION" --no-restore -o /app/publish /p:UseAppHost=false && \
    rm -rf /app/publish/.playwright /app/publish/playwright.ps1

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
EXPOSE 8080
WORKDIR /app
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

COPY --from=restore /opt/playwright ./.playwright
RUN ./.playwright/node/linux-x64/node ./.playwright/package/cli.js install --with-deps --only-shell chromium && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ModeusSchedule.MSAuth.dll"]
