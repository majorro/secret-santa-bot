FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app
RUN mkdir /app/data && chown $APP_UID:$APP_UID /app/data
USER $APP_UID

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["SecretSantaBot.csproj", "."]
RUN dotnet restore "SecretSantaBot.csproj"
COPY . .
RUN dotnet build "SecretSantaBot.csproj" -c $BUILD_CONFIGURATION --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "SecretSantaBot.csproj" -c $BUILD_CONFIGURATION --no-build -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SecretSantaBot.dll"]
