FROM mcr.microsoft.com/dotnet/sdk:6.0-alpine AS build
WORKDIR /source

COPY orchestration-common orchestration-common
COPY maestro maestro
COPY orchestration-db orchestration-db
COPY common common

WORKDIR /source/maestro

RUN dotnet publish -c Debug -o /app/debug

FROM mcr.microsoft.com/dotnet/sdk:6.0-alpine

COPY --from=build /app/debug /app/debug

WORKDIR /app/debug

# ENV VSDBG_DOWNLOAD_URL=<https://aka.ms/getvsdbgsh>
# RUN curl -sSL $VSDBG_DOWNLOAD_URL | bash /dev/stdin -v latest -l /vsdbg

ENTRYPOINT ["dotnet", "./maestro.dll"]