FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_ENVIRONMENT=Production

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Trackarr/Trackarr.csproj", "Trackarr/"]
COPY ["Trackarr.Client/Trackarr.Client.csproj", "Trackarr.Client/"]
COPY ["Trackarr.Core/Trackarr.Core.csproj", "Trackarr.Core/"]
COPY ["Trackarr.Data/Trackarr.Data.csproj", "Trackarr.Data/"]
COPY ["Trackarr.Services/Trackarr.Services.csproj", "Trackarr.Services/"]
COPY ["Trackarr.Routines/Trackarr.Routines.csproj", "Trackarr.Routines/"]
RUN dotnet restore "Trackarr/Trackarr.csproj"
COPY . .
WORKDIR "/src/Trackarr"
RUN dotnet publish "Trackarr.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
RUN mkdir -p /config
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Trackarr.dll"]