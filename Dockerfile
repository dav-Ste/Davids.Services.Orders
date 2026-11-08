# Build from workspace parent directory so sibling ../Davids.Shared is in build context:
#   docker build -f Davids.Services.Orders/Dockerfile -t ghcr.io/davids/davids-services-orders:1.0.0 .
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /workspace

COPY Davids.Shared/Directory.Build.props Davids.Shared/Directory.Packages.props Davids.Shared/global.json ./Davids.Shared/
COPY Davids.Shared/eng/ ./Davids.Shared/eng/
COPY Davids.Shared/src/Davids.ServiceDefaults/Davids.ServiceDefaults.csproj Davids.Shared/src/Davids.ServiceDefaults/packages.lock.json ./Davids.Shared/src/Davids.ServiceDefaults/
COPY Davids.Services.Orders/Directory.Build.props Davids.Services.Orders/Directory.Packages.props Davids.Services.Orders/global.json ./Davids.Services.Orders/
COPY Davids.Services.Orders/src/Davids.Services.Orders.Core/Davids.Services.Orders.Core.csproj Davids.Services.Orders/src/Davids.Services.Orders.Core/packages.lock.json ./Davids.Services.Orders/src/Davids.Services.Orders.Core/
COPY Davids.Services.Orders/src/Davids.Services.Orders.Application/Davids.Services.Orders.Application.csproj Davids.Services.Orders/src/Davids.Services.Orders.Application/packages.lock.json ./Davids.Services.Orders/src/Davids.Services.Orders.Application/
COPY Davids.Services.Orders/src/Davids.Services.Orders.Infrastructure/Davids.Services.Orders.Infrastructure.csproj Davids.Services.Orders/src/Davids.Services.Orders.Infrastructure/packages.lock.json ./Davids.Services.Orders/src/Davids.Services.Orders.Infrastructure/
COPY Davids.Services.Orders/src/Davids.Services.Orders.Api/Davids.Services.Orders.Api.csproj Davids.Services.Orders/src/Davids.Services.Orders.Api/packages.lock.json ./Davids.Services.Orders/src/Davids.Services.Orders.Api/

RUN dotnet restore ./Davids.Services.Orders/src/Davids.Services.Orders.Api/Davids.Services.Orders.Api.csproj --locked-mode

COPY Davids.Shared/src/Davids.ServiceDefaults/ ./Davids.Shared/src/Davids.ServiceDefaults/
COPY Davids.Services.Orders/src/ ./Davids.Services.Orders/src/

RUN dotnet publish ./Davids.Services.Orders/src/Davids.Services.Orders.Api/Davids.Services.Orders.Api.csproj \
      --no-restore -c Release /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

COPY --from=build --chown=10001:10001 /workspace/Davids.Services.Orders/artifacts/publish/Davids.Services.Orders.Api/release .
USER 10001:10001
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD ["dotnet", "--info"]

ENTRYPOINT ["dotnet", "Davids.Services.Orders.Api.dll"]
