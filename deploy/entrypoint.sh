#!/bin/sh
set -eu

mkdir -p "$(dirname "$SISTEMA_AVALIACAO_DB")"

if [ ! -f "$SISTEMA_AVALIACAO_DB" ]; then
    cp /seed/sistema_avaliacao.db "$SISTEMA_AVALIACAO_DB"
fi

exec /usr/bin/supervisord -c /etc/supervisor/conf.d/supervisord.conf
