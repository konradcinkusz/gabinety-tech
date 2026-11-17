# syntax=docker/dockerfile:1
# Gabinety.Reputation. Build context is the repository root:
#   docker build -f src/Gabinety.Reputation/Dockerfile -t gabinety-reputation .
# No secrets are read at build time or written into a layer; configuration arrives as environment variables at run time.

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src
# Restore first (cached until a project file or a package version changes).
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet restore src/Gabinety.Reputation/Gabinety.Reputation.csproj
RUN dotnet publish src/Gabinety.Reputation/Gabinety.Reputation.csproj -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
ARG VCS_REF=unknown
LABEL org.opencontainers.image.title="gabinety-reputation" \
      org.opencontainers.image.source="https://github.com/konradcinkusz/gabinety-tech" \
      org.opencontainers.image.revision="${VCS_REF}"
WORKDIR /app
COPY --from=build /app ./
# The base image ships tzdata (Europe/Warsaw resolves, A-17) and a non-root user "app" (non-zero UID).
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
USER app
EXPOSE 8080
# The aspnet image has no curl or wget; bash is enough to ask /alive (liveness only, no dependencies; P7).
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=3 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /alive HTTP/1.0\\r\\nHost: localhost\\r\\n\\r\\n' >&3 && head -n1 <&3 | grep -q ' 200 '"]
ENTRYPOINT ["dotnet", "Gabinety.Reputation.dll"]
