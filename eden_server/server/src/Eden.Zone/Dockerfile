# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/Eden.Zone/Eden.Zone.csproj
RUN dotnet publish src/Eden.Zone/Eden.Zone.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Diagnostics runtime conservés : dotnet-counters, OTel et les endpoints
# /metrics peuvent être activés par EDEN_OBSERVABILITY_PORT.
ENV ZONE_PORT=5201
EXPOSE 5201
ENTRYPOINT ["dotnet", "Eden.Zone.dll"]
