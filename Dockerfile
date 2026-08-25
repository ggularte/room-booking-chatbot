# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, so a change to source code does not invalidate the restore layer.
COPY RoomBooking.slnx ./
COPY src/RoomBooking.Core/RoomBooking.Core.csproj      src/RoomBooking.Core/
COPY src/RoomBooking.Agent/RoomBooking.Agent.csproj    src/RoomBooking.Agent/
COPY src/RoomBooking.Web/RoomBooking.Web.csproj        src/RoomBooking.Web/
COPY tests/RoomBooking.Tests/RoomBooking.Tests.csproj  tests/RoomBooking.Tests/
RUN dotnet restore src/RoomBooking.Web/RoomBooking.Web.csproj

COPY . .
RUN dotnet publish src/RoomBooking.Web/RoomBooking.Web.csproj -c Release -o /app --no-restore

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# SQLite writes to disk, and a container filesystem does not survive a redeploy. Mounting a volume
# at /data is what keeps bookings; without one the office is empty again after every deploy.
RUN mkdir -p /data && chown -R app:app /data
ENV ConnectionStrings__Bookings="Data Source=/data/bookings.db"

# PORT is read at startup when the platform assigns one; this is the fallback.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER app
ENTRYPOINT ["dotnet", "RoomBooking.Web.dll"]
