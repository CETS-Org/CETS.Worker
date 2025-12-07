# ================================
# Build stage
# ================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy Worker project file
COPY CETS.Worker/CETS.Worker/*.csproj CETS.Worker/CETS.Worker/

# Copy Core project csproj files
COPY CETS.Core/Application/*.csproj CETS.Core/Application/
COPY CETS.Core/Domain/*.csproj CETS.Core/Domain/
COPY CETS.Core/DTOs/*.csproj CETS.Core/DTOs/
COPY CETS.Core/Infrastructure/*.csproj CETS.Core/Infrastructure/
COPY CETS.Core/Utils/*.csproj CETS.Core/Utils/

# Restore dependencies
RUN dotnet restore CETS.Worker/CETS.Worker/CETS.Worker.csproj

# Copy entire solution source
COPY . .

# Publish
RUN dotnet publish CETS.Worker/CETS.Worker/CETS.Worker.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

# ================================
# Runtime stage
# ================================
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV DOTNET_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "CETS.Worker.dll"]
