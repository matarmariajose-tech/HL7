FROM mcr.microsoft.com/dotnet/sdk:8.0.426 AS build
WORKDIR /source
COPY . .
RUN dotnet restore Immunization.sln --locked-mode
RUN dotnet publish src/Immunization.Cli -c Release --no-restore -o /app /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/runtime:8.0.32
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Immunization.Cli.dll"]
