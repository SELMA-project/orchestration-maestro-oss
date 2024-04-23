FROM mcr.microsoft.com/dotnet/sdk:6.0-focal AS build
WORKDIR /source

COPY worker-template worker-template
COPY orchestration-common orchestration-common
COPY worker-easypython worker-easypython
WORKDIR /source/worker-easypython

RUN dotnet publish -r linux-x64 -c Release -o /app/release
RUN dotnet publish -r linux-x64 -c Debug -o /app/debug

FROM mcr.microsoft.com/dotnet/sdk:6.0-focal
WORKDIR /app
COPY --from=build /app .

WORKDIR /app/debug
ENTRYPOINT ["dotnet","worker-easypython.dll"]
# To use release, on docker-compose do
# working_dir: /app/release
