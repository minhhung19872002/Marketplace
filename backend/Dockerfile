# ShopHub API — build with the .NET 8 SDK, run on the ASP.NET 8 runtime
FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /src
COPY Directory.Build.props ShopHub.sln ./
COPY src/ShopHub.Domain/ShopHub.Domain.csproj src/ShopHub.Domain/
COPY src/ShopHub.Application/ShopHub.Application.csproj src/ShopHub.Application/
COPY src/ShopHub.Infrastructure/ShopHub.Infrastructure.csproj src/ShopHub.Infrastructure/
COPY src/ShopHub.Reporting/ShopHub.Reporting.csproj src/ShopHub.Reporting/
COPY src/ShopHub.Api/ShopHub.Api.csproj src/ShopHub.Api/
RUN dotnet restore src/ShopHub.Api/ShopHub.Api.csproj
COPY src/ src/
RUN dotnet publish src/ShopHub.Api/ShopHub.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim
# ICU is needed for vi-VN culture formatting
RUN apt-get update && apt-get install -y --no-install-recommends libicu72 curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    TZ=Asia/Ho_Chi_Minh
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=3s --retries=5 CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "ShopHub.Api.dll"]
