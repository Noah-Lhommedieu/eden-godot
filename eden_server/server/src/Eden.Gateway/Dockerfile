# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/Eden.Gateway/Eden.Gateway.csproj
RUN dotnet publish src/Eden.Gateway/Eden.Gateway.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Diagnostics runtime conservés : dotnet-counters, OTel et les endpoints
# /metrics peuvent être activés par EDEN_OBSERVABILITY_PORT.
ENV GATEWAY_PORT=5560
EXPOSE 5560
ENTRYPOINT ["dotnet", "Eden.Gateway.dll"]
