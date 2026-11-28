FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "PD.Mortgages.Api/PD.Mortgages.Api.csproj"
RUN dotnet publish "PD.Mortgages.Api/PD.Mortgages.Api.csproj" --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM runtime AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PD.Mortgages.Api.dll"]
