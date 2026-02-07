# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY VideoGenerator.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0-noble AS runtime
WORKDIR /app

# Install FFMPEG and c2patool
RUN apt-get update && \
    apt-get install -y ffmpeg curl && \
    curl -L -o /tmp/c2patool.tar.gz \
      https://github.com/contentauth/c2pa-rs/releases/download/c2patool-v0.26.26/c2patool-v0.26.26-x86_64-unknown-linux-gnu.tar.gz && \
    mkdir -p /tmp/c2patool && \
    tar -xzf /tmp/c2patool.tar.gz -C /tmp/c2patool --strip-components=0 && \
    find /tmp/c2patool -name 'c2patool' -type f -exec mv {} /usr/local/bin/c2patool \; && \
    chmod +x /usr/local/bin/c2patool && \
    rm -rf /tmp/c2patool /tmp/c2patool.tar.gz && \
    apt-get purge -y curl && \
    apt-get autoremove -y && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Verify installations
RUN ffmpeg -version && ffprobe -version && c2patool --version

COPY --from=build /app/publish .

# Copy C2PA manifest for AI content labeling
COPY c2pa-manifest.json /app/c2pa-manifest.json

# Expose port (Azure Container Apps uses 8080 by default)
EXPOSE 8080

# Set ASP.NET Core to listen on port 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "VideoGenerator.dll"]
