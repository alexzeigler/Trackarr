FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Trackarr/Trackarr.csproj", "Trackarr/"]
COPY ["Trackarr.Client/Trackarr.Client.csproj", "Trackarr.Client/"]
RUN dotnet restore "Trackarr/Trackarr.csproj"
COPY . .
WORKDIR "/src/Trackarr"
RUN dotnet build "./Trackarr.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Trackarr.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Trackarr.dll"]
