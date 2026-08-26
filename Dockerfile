# Build
#
# Pinned, not floating. The 10.0 tag followed the newest feature band to 10.0.400, whose publish
# emitted wwwroot without _framework — no Blazor runtime, so the deployed page loaded, answered
# every request and did nothing. 10.0.302 is the band this was written and verified against.
FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
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

# Fail here rather than at the far end of a deploy. Without this file the page loads and every
# request succeeds while nothing on it works, which is the most expensive kind of broken: it looks
# fine from outside and takes a browser console to notice.
RUN test -f /app/wwwroot/_framework/blazor.web.js || ( \
      echo "ERROR: publish produced no wwwroot/_framework/blazor.web.js" >&2; \
      echo "The Blazor runtime is missing and the application would be inert." >&2; \
      ls -la /app/wwwroot >&2; \
      exit 1 )

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# SQLite writes to disk, and a container filesystem does not survive a redeploy. Mounting a volume
# at /data is what keeps bookings; without one the office is empty again after every deploy.
#
# This chown applies to the image's own /data. A mounted volume covers it with a fresh directory,
# and some platforms — Railway among them — leave that owned by root, which the non-root user below
# then cannot write to. Where that happens the platform needs telling to run as root: on Railway
# that is RAILWAY_RUN_UID=0. See the README.
RUN mkdir -p /data && chown -R app:app /data
ENV ConnectionStrings__Bookings="Data Source=/data/bookings.db"

# PORT is read at startup when the platform assigns one; this is the fallback.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER app
ENTRYPOINT ["dotnet", "RoomBooking.Web.dll"]
