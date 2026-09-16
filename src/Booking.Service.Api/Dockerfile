# Debian SDK instead of alpine for the build stage — Grpc.Tools 2.71 ships a protoc binary
# linked against glibc that fails on alpine musl with "No such file or directory". The runtime
# stage stays alpine because the publish output is framework-dependent IL DLLs (no --runtime
# flag), which run on either glibc or musl ASP.NET runtimes.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG EVENTHUB_NUGET_FEED=http://host.docker.internal:5555/v3/index.json
ENV EVENTHUB_NUGET_FEED=${EVENTHUB_NUGET_FEED}
WORKDIR /src
COPY ["Directory.Build.props", "Directory.Packages.props", "nuget.config", "./"]
COPY ["src/Booking.Service.Api/Booking.Service.Api.csproj", "src/Booking.Service.Api/"]
COPY ["src/Booking.Service.Application/Booking.Service.Application.csproj", "src/Booking.Service.Application/"]
COPY ["src/Booking.Service.Domain/Booking.Service.Domain.csproj", "src/Booking.Service.Domain/"]
COPY ["src/Booking.Service.Infrastructure/Booking.Service.Infrastructure.csproj", "src/Booking.Service.Infrastructure/"]
RUN dotnet restore "src/Booking.Service.Api/Booking.Service.Api.csproj"
COPY . .
RUN dotnet publish "src/Booking.Service.Api/Booking.Service.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
RUN apk add --no-cache icu-libs wget && \
    adduser -D -h /app appuser
WORKDIR /app
COPY --from=build /app/publish .
USER appuser
ENV ASPNETCORE_URLS=http://+:5000 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
HEALTHCHECK --interval=15s --timeout=3s --start-period=10s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:5000/health/live || exit 1
ENTRYPOINT ["dotnet", "Booking.Service.Api.dll"]
