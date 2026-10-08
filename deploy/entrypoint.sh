#!/bin/sh
set -eu

# Garante que o diretório do banco exista no volume persistente.
# O banco em si é criado/atualizado pelas migrations do EF Core no startup
# (DbInitializer.Initialize -> Database.Migrate). O usuário administrador inicial
# é criado a partir das variáveis de ambiente INITIAL_ADMIN_* quando o banco está vazio.
mkdir -p "$(dirname "$SISTEMA_AVALIACAO_DB")"

exec /usr/bin/supervisord -c /etc/supervisor/conf.d/supervisord.conf
