# Pavlov Shack manager (Blazor Server, .NET 10). Build context: the management/ folder.

# 1. Tailwind CSS: scans the Razor components and writes wwwroot/app.css.
FROM docker.io/library/node:24-alpine AS styles
WORKDIR /src/Pavlov.Manager
COPY src/Pavlov.Manager/package.json src/Pavlov.Manager/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/Pavlov.Manager/Styles ./Styles
COPY src/Pavlov.Manager/Components ./Components
RUN npm run build:css

# 2. .NET publish (Tailwind step skipped, its output comes from stage 1).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Pavlov.Rcon/Pavlov.Rcon.csproj src/Pavlov.Rcon/
COPY src/Pavlov.Manager/Pavlov.Manager.csproj src/Pavlov.Manager/
RUN dotnet restore src/Pavlov.Manager/Pavlov.Manager.csproj
COPY src/ src/
COPY --from=styles /src/Pavlov.Manager/wwwroot/app.css src/Pavlov.Manager/wwwroot/app.css
RUN dotnet publish src/Pavlov.Manager/Pavlov.Manager.csproj -c Release -o /app --no-restore -p:SkipTailwind=true

# 3. Runtime: non-root "app" user, HTTP on 8080. /data holds the end-of-game rules (a volume).
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /data && chown app:app /data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Rcon__Host=pavlov \
    Rcon__Port=9100 \
    Data__Path=/data
EXPOSE 8080
VOLUME ["/data"]
USER app
ENTRYPOINT ["dotnet", "Pavlov.Manager.dll"]
