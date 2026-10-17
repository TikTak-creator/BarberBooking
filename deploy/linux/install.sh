#!/usr/bin/env bash
set -Eeuo pipefail
umask 077
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
[[ $EUID == 0 ]] || { echo 'Run: sudo bash install.sh'; exit 1; }
[[ $(uname -m) == x86_64 ]] || { echo 'This package supports Linux x86_64.'; exit 1; }
[[ ! -e private/appdata/master.key && ! -d private/mysql/mysql ]] || { echo 'An existing database was found. Use manage.sh; install never overwrites a host.'; exit 1; }
if ! command -v docker >/dev/null || ! docker compose version >/dev/null 2>&1; then
    . /etc/os-release
    [[ ${ID:-} == ubuntu || ${ID:-} == debian ]] || { echo 'Install Docker Engine + Compose from docs.docker.com, then rerun.'; exit 1; }
    read -r -p 'Install Docker Engine and Compose from the official Docker apt repository? [y/N] ' answer
    [[ $answer == y || $answer == Y ]] || exit 1
    for package in docker.io docker-compose podman-docker containerd runc; do
        if dpkg-query -W -f='${Status}' "$package" 2>/dev/null | grep -q 'install ok installed'; then
            echo "Existing package $package found. Resolve Docker package conflicts using the official guide; no package was removed."; exit 1
        fi
    done
    apt-get update
    apt-get install -y ca-certificates curl gnupg openssl python3 python3-cryptography
    install -m 0755 -d /etc/apt/keyrings
    curl --fail --location --proto '=https' --tlsv1.2 "https://download.docker.com/linux/$ID/gpg" -o /etc/apt/keyrings/bladez-docker.asc
    fingerprint=$(gpg --batch --show-keys --with-colons /etc/apt/keyrings/bladez-docker.asc | awk -F: '$1=="fpr"{print $10;exit}')
    [[ $fingerprint == 9DC858229FC7DD38854AE2D88D81803C0EBFCD88 ]] || { echo 'Docker signing key fingerprint mismatch.'; exit 1; }
    chmod a+r /etc/apt/keyrings/bladez-docker.asc
    printf 'deb [arch=amd64 signed-by=/etc/apt/keyrings/bladez-docker.asc] https://download.docker.com/linux/%s %s stable\n' "$ID" "${UBUNTU_CODENAME:-$VERSION_CODENAME}" > /etc/apt/sources.list.d/bladez-docker.list
    apt-get update
    apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
    systemctl enable --now docker
fi
command -v openssl >/dev/null && command -v python3 >/dev/null || { echo 'Install openssl and python3, then rerun.'; exit 1; }
docker info >/dev/null
if docker compose ps -q 2>/dev/null | grep -q .; then echo 'The bladez Compose project is already in use.'; exit 1; fi
echo 'BLADEZ / HTTPS HOST'
echo 'Prepare a domain pointing to this server. TCP 80/443 and UDP 443 must be reachable.'
read -r -p 'Domain (example: booking.example.com): ' domain
[[ $domain =~ ^([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}$ ]] || { echo 'Enter an ASCII DNS name, without https:// or a path.'; exit 1; }
read -r -p 'Certificate contact email: ' email
[[ $email =~ ^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,63}$ ]] || { echo 'Invalid contact email.'; exit 1; }
read -r -p 'Shop name: ' shop
read -r -p 'Owner display name: ' owner
read -r -p 'Owner login (Latin letters, digits, ._-): ' login
[[ $login =~ ^[a-zA-Z0-9._-]{3,40}$ && -n $shop && ${#shop} -le 100 && -n $owner && ${#owner} -le 100 ]] || { echo 'Check shop, owner and login.'; exit 1; }
read -r -s -p 'Owner password (12–128 characters, Aa and digits): ' password; echo
read -r -s -p 'Repeat password: ' repeat; echo
[[ $password == "$repeat" && ${#password} -ge 12 && ${#password} -le 128 && $password =~ [a-z] && $password =~ [A-Z] && $password =~ [0-9] ]] || { echo 'Passwords do not match or are too weak.'; exit 1; }
mkdir -p private/{appdata,mysql,db-certs,init,caddy-data,caddy-config}
chmod 700 private
root_password=$(openssl rand -hex 32); app_password=$(openssl rand -hex 32); migration_password=$(openssl rand -hex 32)
printf '%s' "$root_password" > private/root-password
printf '[client]\nuser=root\npassword=%s\nssl-mode=VERIFY_CA\nssl-ca=/certs/ca.pem\n' "$root_password" > private/root-client.cnf
printf 'BLADEZ_DOMAIN=%s\nBLADEZ_ACME_EMAIL=%s\n' "$domain" "$email" > .env
printf "CREATE USER 'bladez_app'@'%%' IDENTIFIED BY '%s';\nGRANT SELECT, INSERT, UPDATE, DELETE ON bladez_business.* TO 'bladez_app'@'%%';\nCREATE USER 'bladez_migrate'@'%%' IDENTIFIED BY '%s';\nGRANT ALL PRIVILEGES ON bladez_business.* TO 'bladez_migrate'@'%%';\n" "$app_password" "$migration_password" > private/init/10-bladez.sql
openssl req -x509 -newkey rsa:3072 -sha256 -nodes -days 3650 -subj '/CN=Bladez private MySQL CA' -keyout private/ca-key.pem -out private/db-certs/ca.pem 2>/dev/null
openssl req -newkey rsa:3072 -sha256 -nodes -subj '/CN=db' -keyout private/db-certs/server-key.pem -out private/server.csr 2>/dev/null
printf 'subjectAltName=DNS:db,DNS:localhost,IP:127.0.0.1\nextendedKeyUsage=serverAuth\n' > private/server.ext
openssl x509 -req -in private/server.csr -CA private/db-certs/ca.pem -CAkey private/ca-key.pem -CAcreateserial -days 825 -sha256 -extfile private/server.ext -out private/db-certs/server.pem 2>/dev/null
cp private/db-certs/ca.pem private/ca.pem
openssl genrsa -traditional -out private/mysql-client-key.pem 3072 2>/dev/null
openssl req -new -key private/mysql-client-key.pem -subj '/CN=bladez-app' -out private/client.csr 2>/dev/null
printf 'basicConstraints=CA:FALSE\nkeyUsage=digitalSignature,keyEncipherment\nextendedKeyUsage=clientAuth\n' > private/client.ext
openssl x509 -req -in private/client.csr -CA private/db-certs/ca.pem -CAkey private/ca-key.pem -CAcreateserial -days 825 -sha256 -extfile private/client.ext -out private/mysql-client.pem 2>/dev/null
printf 'Server=db;Port=3306;Database=bladez_business;User ID=bladez_app;Password=%s;SslMode=VerifyFull;SslCa=/run/secrets/mysql-ca.pem;SslCert=/run/secrets/mysql-client.pem;SslKey=/run/secrets/mysql-client-key.pem;AllowPublicKeyRetrieval=false;ConnectionTimeout=10' "$app_password" > private/app-connection
printf 'Server=db;Port=3306;Database=bladez_business;User ID=bladez_migrate;Password=%s;SslMode=VerifyFull;SslCa=/run/secrets/mysql-ca.pem;SslCert=/run/secrets/mysql-client.pem;SslKey=/run/secrets/mysql-client-key.pem;AllowPublicKeyRetrieval=false;ConnectionTimeout=10' "$migration_password" > private/migration-connection
printf '%s\0%s\0%s\0%s' "$login" "$owner" "$password" "$shop" | python3 -c 'import sys,json; u,n,p,s=sys.stdin.buffer.read().decode().split("\0"); json.dump(dict(UserName=u,DisplayName=n,Password=p,ShopName=s,SeedCatalog=True),sys.stdout,ensure_ascii=False)' > private/bootstrap.json
unset password repeat root_password app_password migration_password
chmod 600 private/* 2>/dev/null || true
chmod 700 private private/{appdata,mysql,db-certs,init,caddy-data,caddy-config}
chown -R 999:999 private/mysql private/db-certs private/init
chown 999:999 private/root-password private/root-client.cnf
chmod 600 private/db-certs/* private/init/*
chown -R 1654:1654 private/appdata private/app-connection private/migration-connection private/bootstrap.json
chown 1654:1654 private/mysql-client.pem private/mysql-client-key.pem
chmod 644 private/ca.pem
cleanup() { rm -f -- private/bootstrap.json; }
trap cleanup EXIT
docker compose config --quiet
docker compose build --pull app
docker compose up -d --wait --wait-timeout 360 db
docker compose --profile operations run --rm migrate --initialize
docker compose exec -T db mysql --defaults-extra-file=/run/secrets/root-client.cnf -e "DROP USER 'bladez_migrate'@'%';"
rm -f -- private/init/10-bladez.sql private/migration-connection private/bootstrap.json
# Empty placeholders let a later, explicit migration operation mount temporary credentials.
touch private/migration-connection private/bootstrap.json; chown 1654:1654 private/migration-connection private/bootstrap.json; chmod 600 private/migration-connection private/bootstrap.json
touch private/installed
docker compose up -d app proxy
ready=false
for attempt in {1..60}; do
    if docker compose exec -T proxy wget -q -O- --header "Host: $domain" --header 'X-Forwarded-Proto: https' http://app:8080/health/ready 2>/dev/null | grep -q true; then ready=true; break; fi
    sleep 2
done
[[ $ready == true ]] || { echo 'The site did not confirm database/key readiness. Data is preserved. Check: sudo bash manage.sh logs'; exit 1; }
echo "Installed. Open https://$domain . Certificate issuance can take a few minutes."
echo 'Server status: sudo bash manage.sh status'
echo 'Back up now: sudo bash manage.sh backup'
