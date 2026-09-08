# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# Copy project file
COPY ["src/PharmacyOrder.Api/PharmacyOrder.Api.csproj", "src/PharmacyOrder.Api/"]

# Restore dependencies
RUN dotnet restore "src/PharmacyOrder.Api/PharmacyOrder.Api.csproj"

# Copy source code
COPY . .

# Publish application
RUN dotnet publish "src/PharmacyOrder.Api/PharmacyOrder.Api.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false


# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "PharmacyOrder.Api.dll"]