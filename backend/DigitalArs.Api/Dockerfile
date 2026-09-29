FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/DigitalArs.Api/DigitalArs.Api.csproj backend/DigitalArs.Api/
RUN dotnet restore backend/DigitalArs.Api/DigitalArs.Api.csproj
COPY backend/DigitalArs.Api/ backend/DigitalArs.Api/
RUN dotnet publish backend/DigitalArs.Api/DigitalArs.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=10000
ENV ASPNETCORE_ENVIRONMENT=Production
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 10000
ENTRYPOINT ["dotnet", "DigitalArs.Api.dll"]
