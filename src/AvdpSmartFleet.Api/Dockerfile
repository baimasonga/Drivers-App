FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/AvdpSmartFleet.Api/AvdpSmartFleet.Api.csproj", "AvdpSmartFleet.Api/"]
RUN dotnet restore "AvdpSmartFleet.Api/AvdpSmartFleet.Api.csproj"
COPY src/AvdpSmartFleet.Api/. AvdpSmartFleet.Api/
WORKDIR /src/AvdpSmartFleet.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
VOLUME ["/data", "/app/uploads"]
ENTRYPOINT ["dotnet", "AvdpSmartFleet.Api.dll"]
