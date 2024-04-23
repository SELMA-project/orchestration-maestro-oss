FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /app/source/

COPY orchestration-common orchestration-common
COPY worker-template worker-template
COPY worker-sidecar-modules worker-sidecar-modules
COPY worker-js-lib worker-js-lib
COPY worker-sidecar worker-sidecar

WORKDIR /app/source/worker-sidecar

RUN dotnet restore
RUN dotnet publish -c Debug -o /app/release --no-restore

FROM mcr.microsoft.com/dotnet/sdk:6.0
WORKDIR /app
COPY --from=build /app/release .

ENTRYPOINT ["dotnet", "./worker-sidecar.dll"]
