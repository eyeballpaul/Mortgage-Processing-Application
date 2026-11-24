FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "PD.Mortgages.Staff.Web/PD.Mortgages.Staff.Web.csproj"
RUN dotnet publish "PD.Mortgages.Staff.Web/PD.Mortgages.Staff.Web.csproj" --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM runtime AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PD.Mortgages.Staff.Web.dll"]
