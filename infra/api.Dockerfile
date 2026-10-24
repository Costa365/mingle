FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS build
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY server/ server/
RUN dotnet restore server/TeamGames.Api/TeamGames.Api.csproj --locked-mode
RUN dotnet publish server/TeamGames.Api/TeamGames.Api.csproj --no-restore -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble AS runtime
USER root
RUN apt-get update \
    && apt-get install --no-install-recommends -y curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data/keys \
    && chown -R app:app /data \
    && chmod 700 /data /data/keys
WORKDIR /app
COPY --from=build --chown=app:app /app/ ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Storage__DataDirectory=/data
USER app
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=30s --retries=3 \
    CMD curl --fail --silent http://127.0.0.1:8080/api/health/ready || exit 1
ENTRYPOINT ["dotnet", "TeamGames.Api.dll"]
