# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Package-resolution layer: restore only against project files so that source edits
# do not invalidate the NuGet cache.
COPY global.json Directory.Build.props ./
COPY src/HookGuard.Domain/HookGuard.Domain.csproj src/HookGuard.Domain/
COPY src/HookGuard.Application/HookGuard.Application.csproj src/HookGuard.Application/
COPY src/HookGuard.Infrastructure/HookGuard.Infrastructure.csproj src/HookGuard.Infrastructure/
COPY src/HookGuard.Api/HookGuard.Api.csproj src/HookGuard.Api/
RUN dotnet restore src/HookGuard.Api/HookGuard.Api.csproj

COPY src/ src/
RUN dotnet publish src/HookGuard.Api/HookGuard.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "HookGuard.Api.dll"]
