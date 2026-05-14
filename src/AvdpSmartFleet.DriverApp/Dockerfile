FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/AvdpSmartFleet.DriverApp/AvdpSmartFleet.DriverApp.csproj", "AvdpSmartFleet.DriverApp/"]
RUN dotnet restore "AvdpSmartFleet.DriverApp/AvdpSmartFleet.DriverApp.csproj"
COPY src/AvdpSmartFleet.DriverApp/. AvdpSmartFleet.DriverApp/
WORKDIR /src/AvdpSmartFleet.DriverApp
RUN dotnet publish -c Release -o /app/publish

FROM nginx:alpine AS runtime
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY src/AvdpSmartFleet.DriverApp/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
