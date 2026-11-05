FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_ENVIRONMENT=Production
# Time zone for times shown in the UI; override with -e TZ=America/New_York.
ENV TZ=Etc/UTC
# User/group the app runs as (Unraid default nobody:users). See docker-entrypoint.sh.
ENV PUID=99
ENV PGID=100

# curl for the healthcheck, gosu to drop root, tzdata so TZ works.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl gosu tzdata \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Trackarr/Trackarr.csproj", "Trackarr/"]
COPY ["Trackarr.Core/Trackarr.Core.csproj", "Trackarr.Core/"]
COPY ["Trackarr.Data/Trackarr.Data.csproj", "Trackarr.Data/"]
COPY ["Trackarr.Services/Trackarr.Services.csproj", "Trackarr.Services/"]
COPY ["Trackarr.Routines/Trackarr.Routines.csproj", "Trackarr.Routines/"]
COPY ["Directory.Packages.props", "./"]
RUN dotnet restore "Trackarr/Trackarr.csproj"
COPY . .
WORKDIR "/src/Trackarr"
RUN dotnet publish "Trackarr.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
RUN mkdir -p /config
COPY --from=build /app/publish .
COPY Trackarr/docker-entrypoint.sh /docker-entrypoint.sh
# Strip CRLF in case the script was checked out on Windows.
RUN sed -i 's/\r$//' /docker-entrypoint.sh && chmod +x /docker-entrypoint.sh

HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["/docker-entrypoint.sh"]
