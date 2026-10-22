# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props ./
COPY src/HookGuard.Domain/HookGuard.Domain.csproj src/HookGuard.Domain/
COPY src/HookGuard.Application/HookGuard.Application.csproj src/HookGuard.Application/
COPY src/HookGuard.Infrastructure/HookGuard.Infrastructure.csproj src/HookGuard.Infrastructure/
COPY src/HookGuard.Workers/HookGuard.Workers.csproj src/HookGuard.Workers/
RUN dotnet restore src/HookGuard.Workers/HookGuard.Workers.csproj

COPY src/ src/
RUN dotnet publish src/HookGuard.Workers/HookGuard.Workers.csproj \
    --configuration Release \
    --no-restore \
    --output /app

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "HookGuard.Workers.dll"]
