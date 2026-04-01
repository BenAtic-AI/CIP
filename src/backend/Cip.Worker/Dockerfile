# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
USER $APP_UID

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["src/backend/Cip.Worker/Cip.Worker.csproj", "src/backend/Cip.Worker/"]
COPY ["src/backend/Cip.Application/Cip.Application.csproj", "src/backend/Cip.Application/"]
COPY ["src/backend/Cip.Contracts/Cip.Contracts.csproj", "src/backend/Cip.Contracts/"]
COPY ["src/backend/Cip.Domain/Cip.Domain.csproj", "src/backend/Cip.Domain/"]
COPY ["src/backend/Cip.Infrastructure/Cip.Infrastructure.csproj", "src/backend/Cip.Infrastructure/"]
COPY ["src/backend/Integrations.AzureAi/Integrations.AzureAi.csproj", "src/backend/Integrations.AzureAi/"]
COPY ["src/backend/Integrations.Cosmos/Integrations.Cosmos.csproj", "src/backend/Integrations.Cosmos/"]
COPY ["src/backend/Integrations.Storage/Integrations.Storage.csproj", "src/backend/Integrations.Storage/"]

RUN dotnet restore "src/backend/Cip.Worker/Cip.Worker.csproj"

COPY ["src/backend/", "src/backend/"]

RUN dotnet publish "src/backend/Cip.Worker/Cip.Worker.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false \
    --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Cip.Worker.dll"]
