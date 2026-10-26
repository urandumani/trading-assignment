FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props ./
COPY src/TradeSim.Domain/TradeSim.Domain.csproj                 src/TradeSim.Domain/
COPY src/TradeSim.Application/TradeSim.Application.csproj       src/TradeSim.Application/
COPY src/TradeSim.Infrastructure/TradeSim.Infrastructure.csproj src/TradeSim.Infrastructure/
COPY src/TradeSim.Api/TradeSim.Api.csproj                       src/TradeSim.Api/
COPY src/TradeSim.Worker/TradeSim.Worker.csproj                 src/TradeSim.Worker/
RUN dotnet restore src/TradeSim.Api/TradeSim.Api.csproj \
 && dotnet restore src/TradeSim.Worker/TradeSim.Worker.csproj

COPY src/ src/
RUN dotnet publish src/TradeSim.Api/TradeSim.Api.csproj -c Release -o /out/api --no-restore \
 && dotnet publish src/TradeSim.Worker/TradeSim.Worker.csproj -c Release -o /out/worker --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out/api .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TradeSim.Api.dll"]

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS worker
WORKDIR /app
COPY --from=build /out/worker .
USER $APP_UID
ENTRYPOINT ["dotnet", "TradeSim.Worker.dll"]
