#!/bin/sh
set -eu

# Garante que o diretório do banco exista no volume persistente.
# O banco em si é criado/atualizado pelas migrations do EF Core no startup
# (DbInitializer.Initialize -> Database.Migrate). O usuário administrador inicial
# é criado a partir das variáveis de ambiente INITIAL_ADMIN_* quando o banco está vazio.
mkdir -p "$(dirname "$SISTEMA_AVALIACAO_DB")"

# Garante que a raiz de uploads exista no volume persistente (imagens de questão e
# PDFs de folhas de correção são gravados aqui — ver CaminhoArmazenamento).
if [ -n "${SISTEMA_AVALIACAO_UPLOADS:-}" ]; then
  mkdir -p "$SISTEMA_AVALIACAO_UPLOADS"
fi

exec /usr/bin/supervisord -c /etc/supervisor/conf.d/supervisord.conf
