#!/usr/bin/env bash
set -Eeuo pipefail
umask 077
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
[[ $EUID == 0 ]] || { echo 'Run with sudo.'; exit 1; }
[[ -f private/installed ]] || { echo 'Complete install.sh first (or use restore into a fresh package).'; [[ ${1:-} == restore ]] || exit 1; }
case "${1:-status}" in
  status) docker compose ps; docker compose exec -T db mysql --defaults-extra-file=/run/secrets/root-client.cnf -Nse 'SELECT 1';;
  start) docker compose up -d --wait --wait-timeout 360 db; docker compose up -d app proxy;;
  stop) docker compose stop -t 60 proxy app db;;
  logs) docker compose logs --tail 80 app proxy;;
  backup)
    command -v python3 >/dev/null && python3 -c 'import cryptography' || { echo 'Install python3-cryptography with apt, then rerun.'; exit 1; }
    mkdir -p backups; chmod 700 backups
    destination="backups/bladez-$(date -u +%Y%m%dT%H%M%SZ).bladez-linux"
    read -r -s -p 'Backup password (12+ characters): ' password; echo
    read -r -s -p 'Repeat password: ' repeat; echo
    [[ $password == "$repeat" && ${#password} -ge 12 ]] || { echo 'Passwords do not match or are too short.'; exit 1; }
    # Cold backup is consistent across MySQL and the separate application key.
    docker compose stop -t 60 proxy app db
    trap 'unset password repeat; docker compose up -d db app proxy' EXIT
    printf '%s' "$password" | python3 backup.py create "$destination"
    unset password repeat
    echo "Encrypted backup: $destination . Keep its password separately."
    ;;
  restore)
    [[ -n ${2:-} && -f $2 ]] || { echo 'Usage: sudo bash manage.sh restore /path/copy.bladez-linux'; exit 1; }
    [[ ! -e private/appdata/master.key && ! -d private/mysql/mysql ]] || { echo 'Restore only into a fresh package. Existing databases are never overwritten.'; exit 1; }
    read -r -s -p 'Backup password: ' password; echo
    printf '%s' "$password" | python3 backup.py restore "$2"; unset password
    echo 'Restored. Check .env domain/DNS, then: sudo bash manage.sh start'
    ;;
  update)
    echo 'Create and test an encrypted backup before an update. Use the matching source package.'
    read -r -p 'Backup verified? Type UPDATE: ' answer; [[ $answer == UPDATE ]] || exit 1
    migration_password=$(openssl rand -hex 32)
    printf 'Server=db;Port=3306;Database=bladez_business;User ID=bladez_migrate;Password=%s;SslMode=VerifyFull;SslCa=/run/secrets/mysql_ca;ConnectionTimeout=10' "$migration_password" > private/migration-connection
    chown 1654:1654 private/migration-connection; chmod 600 private/migration-connection
    cleanup() { docker compose exec -T db mysql --defaults-extra-file=/run/secrets/root-client.cnf -e "DROP USER IF EXISTS 'bladez_migrate'@'%';" || true; : > private/migration-connection; unset migration_password; }
    trap cleanup EXIT
    docker compose stop -t 60 app
    printf "CREATE USER IF NOT EXISTS 'bladez_migrate'@'%%' IDENTIFIED BY '%s'; ALTER USER 'bladez_migrate'@'%%' IDENTIFIED BY '%s'; GRANT ALL PRIVILEGES ON bladez_business.* TO 'bladez_migrate'@'%%';\n" "$migration_password" "$migration_password" | docker compose exec -T db mysql --defaults-extra-file=/run/secrets/root-client.cnf
    docker compose build --pull app
    docker compose --profile operations run --rm migrate --migrate
    cleanup; trap - EXIT
    docker compose up -d app proxy
    ;;
  *) echo 'Commands: status | start | stop | logs | backup | restore FILE | update'; exit 1;;
esac
