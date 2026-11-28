FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore PD.Mortgages.OutboxPublisher/PD.Mortgages.OutboxPublisher.csproj
RUN dotnet publish PD.Mortgages.OutboxPublisher/PD.Mortgages.OutboxPublisher.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "PD.Mortgages.OutboxPublisher.dll"]
