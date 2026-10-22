# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props ./
COPY src/HookGuard.FakeMerchant/HookGuard.FakeMerchant.csproj src/HookGuard.FakeMerchant/
RUN dotnet restore src/HookGuard.FakeMerchant/HookGuard.FakeMerchant.csproj

COPY src/HookGuard.FakeMerchant/ src/HookGuard.FakeMerchant/
RUN dotnet publish src/HookGuard.FakeMerchant/HookGuard.FakeMerchant.csproj \
    --configuration Release \
    --no-restore \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "HookGuard.FakeMerchant.dll"]
