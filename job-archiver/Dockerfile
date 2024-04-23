FROM mcr.microsoft.com/dotnet/sdk:6.0-alpine AS build
WORKDIR /source

COPY common common
COPY orchestration-common orchestration-common
COPY orchestration-db orchestration-db
COPY job-archiver job-archiver

WORKDIR /source/job-archiver
RUN dotnet publish -c Debug -o /app/debug

WORKDIR /app/debug
ENTRYPOINT ["dotnet", "./job-archiver.dll"]
