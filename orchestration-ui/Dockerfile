FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /source

COPY orchestration-ui orchestration-ui
COPY orchestration-common orchestration-common
COPY orchestration-db orchestration-db
COPY worker-js-lib worker-js-lib
COPY worker-template worker-template
COPY common common

WORKDIR /source/orchestration-ui

# due to path conflict remove these
#RUN rm /source/worker-js/appsettings.json

RUN dotnet restore
RUN dotnet publish -c release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/sdk:6.0

# Install and nodejs se we can build the admin UI json editor component
RUN apt-get update && apt-get upgrade -y && apt-get install nodejs -y && apt-get install npm -y

WORKDIR /source
COPY --from=build /out ./

ENTRYPOINT ["dotnet", "./orchestration-ui.dll"]