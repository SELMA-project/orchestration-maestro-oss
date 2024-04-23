FROM mcr.microsoft.com/dotnet/sdk:6.0-alpine AS build
WORKDIR /source

COPY orchestration-common orchestration-common
COPY worker-template worker-template
COPY worker-js-lib worker-js-lib
COPY worker-js worker-js

WORKDIR /source/worker-js

RUN dotnet publish -c Debug -o /app/debug

FROM mcr.microsoft.com/dotnet/sdk:6.0-alpine
COPY --from=build /app/debug /app/debug

WORKDIR /app/debug
ENTRYPOINT ["dotnet", "./worker-js.dll"]
# To use release, on docker-compose do
# working_dir: /app/release