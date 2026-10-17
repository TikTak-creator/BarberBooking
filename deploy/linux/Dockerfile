FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src
COPY Bladez.Core/Bladez.Core.csproj Bladez.Core/packages.lock.json Bladez.Core/
COPY BarberBooking.Web/BarberBooking.Web.csproj BarberBooking.Web/packages.lock.json BarberBooking.Web/
RUN dotnet restore BarberBooking.Web --locked-mode
COPY Bladez.Core/ Bladez.Core/
COPY BarberBooking.Web/ BarberBooking.Web/
RUN dotnet publish BarberBooking.Web -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12
WORKDIR /app
COPY --from=build /out .
USER 1654:1654
ENV ASPNETCORE_ENVIRONMENT=Production BLADEZ_DATA=/var/lib/bladez BLADEZ_LISTEN_URL=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "BarberBooking.Web.dll"]
