FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY SistemaAvaliacao.Web/SistemaAvaliacao.Web.csproj SistemaAvaliacao.Web/
COPY SistemaAvaliacao.CorrecaoWorker/SistemaAvaliacao.CorrecaoWorker.csproj SistemaAvaliacao.CorrecaoWorker/

RUN dotnet restore SistemaAvaliacao.Web/SistemaAvaliacao.Web.csproj \
    && dotnet restore SistemaAvaliacao.CorrecaoWorker/SistemaAvaliacao.CorrecaoWorker.csproj

COPY . .

RUN dotnet publish SistemaAvaliacao.Web/SistemaAvaliacao.Web.csproj -c Release --no-restore -o /out/web \
    && dotnet publish SistemaAvaliacao.CorrecaoWorker/SistemaAvaliacao.CorrecaoWorker.csproj -c Release --no-restore -o /out/worker

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS final

RUN apt-get update \
    && apt-get install -y --no-install-recommends supervisor libgdiplus libgl1 libglib2.0-0 libgomp1 libfontconfig1 libxrender1 libxext6 libgtk-3-0 libatomic1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=build /out/web ./web
COPY --from=build /out/worker ./worker
COPY deploy/supervisord.conf /etc/supervisor/conf.d/supervisord.conf
COPY deploy/entrypoint.sh /entrypoint.sh

RUN chmod +x /entrypoint.sh

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    SISTEMA_AVALIACAO_DB=/data/sistema_avaliacao.db \
    DATA_PROTECTION_KEYS_PATH=/data/keys

EXPOSE 8080

ENTRYPOINT ["/entrypoint.sh"]
