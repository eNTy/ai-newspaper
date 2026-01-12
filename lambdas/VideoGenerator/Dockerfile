# Use the official Azure Functions .NET 8 image as base
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0 AS base
WORKDIR /home/site/wwwroot

# Install FFMPEG
RUN apt-get update && \
    apt-get install -y ffmpeg && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Verify FFMPEG installation
RUN ffmpeg -version && ffprobe -version

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["VideoGenerator.csproj", "./"]
RUN dotnet restore "VideoGenerator.csproj"

# Copy everything else and build
COPY . .
RUN dotnet build "VideoGenerator.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "VideoGenerator.csproj" -c Release -o /app/publish

# Final stage
FROM base AS final
WORKDIR /home/site/wwwroot
COPY --from=publish /app/publish .

# Set environment variables for Azure Functions
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true
