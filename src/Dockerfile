FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine3.22 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY . .
RUN dotnet restore "ModeusSchedule.MSAuth.csproj"
RUN dotnet publish "ModeusSchedule.MSAuth.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
EXPOSE 8080
WORKDIR /app

RUN apt-get update && \
    apt-get install -y --no-install-recommends wget && \
    wget -q https://github.com/PowerShell/PowerShell/releases/download/v7.5.2/powershell_7.5.2-1.deb_amd64.deb && \
    apt-get install -y ./powershell_7.5.2-1.deb_amd64.deb && \
    rm -f powershell_7.5.2-1.deb_amd64.deb && \
    rm -rf /var/lib/apt/lists/*

ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

COPY --from=build /app/publish .
RUN pwsh ./playwright.ps1 install --with-deps chromium

ENTRYPOINT ["dotnet", "ModeusSchedule.MSAuth.dll"]