FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Girus.slnx .
COPY src/Girus.Api/Girus.Api.csproj src/Girus.Api/
COPY src/Girus.Application/Girus.Application.csproj src/Girus.Application/
COPY src/Girus.Domain/Girus.Domain.csproj src/Girus.Domain/
COPY src/Girus.Infra/Girus.Infra.csproj src/Girus.Infra/
COPY src/Girus.Worker/Girus.Worker.csproj src/Girus.Worker/
COPY tests/Girus.UnitTests/Girus.UnitTests.csproj tests/Girus.UnitTests/
COPY tests/Girus.IntegrationTests/Girus.IntegrationTests.csproj tests/Girus.IntegrationTests/
RUN dotnet restore src/Girus.Api/Girus.Api.csproj

COPY . .
RUN dotnet publish src/Girus.Api/Girus.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Girus.Api.dll"]
