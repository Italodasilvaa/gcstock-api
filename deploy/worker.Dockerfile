FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY GCStock.slnx .
COPY src/GCStock.Api/GCStock.Api.csproj src/GCStock.Api/
COPY src/GCStock.Application/GCStock.Application.csproj src/GCStock.Application/
COPY src/GCStock.Domain/GCStock.Domain.csproj src/GCStock.Domain/
COPY src/GCStock.Infra/GCStock.Infra.csproj src/GCStock.Infra/
COPY src/GCStock.Worker/GCStock.Worker.csproj src/GCStock.Worker/
COPY tests/GCStock.UnitTests/GCStock.UnitTests.csproj tests/GCStock.UnitTests/
COPY tests/GCStock.IntegrationTests/GCStock.IntegrationTests.csproj tests/GCStock.IntegrationTests/
RUN dotnet restore src/GCStock.Worker/GCStock.Worker.csproj

COPY . .
RUN dotnet publish src/GCStock.Worker/GCStock.Worker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "GCStock.Worker.dll"]
