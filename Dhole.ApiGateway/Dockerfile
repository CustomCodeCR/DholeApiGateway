# syntax=docker/dockerfile:1

# Base image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
RUN apt-get update && \
  apt-get install -y --no-install-recommends tzdata apt-utils libgdiplus libc6-dev && \
  ln -sf /usr/lib/libgdiplus.so /usr/lib/gdiplus.dll && \
  rm -rf /var/lib/apt/lists/*

WORKDIR /app
EXPOSE 8080

ENV TZ=America/Costa_Rica
ENV ASPNETCORE_URLS=http://+:8080

# Build image
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG GITHUB_ACTOR=github-actions
WORKDIR /src

COPY ["NuGet.Config", "./NuGet.Config"]
COPY ["Dhole.ApiGateway/Dhole.ApiGateway.csproj", "Dhole.ApiGateway/"]

RUN --mount=type=secret,id=github_token,required=true \
  export NuGetPackageSourceCredentials_github="Username=${GITHUB_ACTOR};Password=$(cat /run/secrets/github_token);ValidAuthenticationTypes=Basic" && \
  dotnet restore "Dhole.ApiGateway/Dhole.ApiGateway.csproj" --configfile /src/NuGet.Config

COPY ["Dhole.ApiGateway/", "Dhole.ApiGateway/"]

WORKDIR "/src/Dhole.ApiGateway"
RUN dotnet build "Dhole.ApiGateway.csproj" \
  -c $BUILD_CONFIGURATION \
  -o /app/build \
  --no-restore

# Publish image
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR "/src/Dhole.ApiGateway"

RUN dotnet publish "Dhole.ApiGateway.csproj" \
  -c $BUILD_CONFIGURATION \
  -o /app/publish \
  --no-restore \
  /p:UseAppHost=false

# Final image
FROM base AS final
WORKDIR /app

COPY --from=publish /app/publish .

RUN chmod 755 /app

ENTRYPOINT ["dotnet", "Dhole.ApiGateway.dll"]
