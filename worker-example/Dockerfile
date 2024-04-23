FROM mcr.microsoft.com/dotnet/sdk:6.0-focal AS build
WORKDIR /source

COPY orchestration-common orchestration-common
COPY worker-template worker-template
COPY worker-example worker-example
WORKDIR /source/worker-example

# RUN dotnet publish -r linux-x64 -c Release -o /app/release
RUN dotnet publish -r linux-x64 -c Debug -o /app/debug

FROM mcr.microsoft.com/dotnet/sdk:6.0-focal
WORKDIR /app
COPY --from=build /app .

WORKDIR /app/debug
ENTRYPOINT ["./worker-example"]
# To use release, on docker-compose do
# working_dir: /app/release