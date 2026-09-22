# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Manifests first, so the restore stays cached while they do not change.
# Neither the solution nor the test projects take part: the image publishes one project,
# and .dockerignore keeps tests out of the build context on purpose.
COPY Directory.Build.props ./
COPY src/domain/*.csproj                        src/domain/
COPY src/application/*.csproj                   src/application/
COPY src/adapters/inbound/api/*.csproj          src/adapters/inbound/api/
COPY src/adapters/outbound/persistence/*.csproj src/adapters/outbound/persistence/
COPY src/adapters/outbound/storage/*.csproj     src/adapters/outbound/storage/
COPY src/adapters/outbound/security/*.csproj    src/adapters/outbound/security/
COPY src/bootstrap/*.csproj                     src/bootstrap/
RUN dotnet restore src/bootstrap/SimpleStockFlow.Bootstrap.csproj

COPY src/ src/
RUN dotnet publish src/bootstrap/SimpleStockFlow.Bootstrap.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Binary storage: LocalFileStorage writes here, never inside the image.
# The .NET 8 base image already ships a non-root user named app, so creating one fails.
RUN mkdir -p /var/lib/simple-stock-flow/media && chown -R app:app /var/lib/simple-stock-flow
USER app

COPY --from=build /app ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SimpleStockFlow.Bootstrap.dll"]
