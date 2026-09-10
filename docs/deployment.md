# Production deployment

The app ships as a single container image, built from
[`src/AndreGoepel.FinanceApp/Dockerfile`](../src/AndreGoepel.FinanceApp/Dockerfile)
and pushed to GHCR on every push to `main` (`.github/workflows/docker-image.yml`).
The workflow publishes convenience tags and records the immutable manifest
digest; production selects only a reviewed digest-qualified reference. The app
expects a Postgres database and a TLS-terminating reverse proxy in front of it.
This guide describes the reference setup: Docker Compose with a shared nginx
proxy and a shared Postgres container on one VPS, reachable as
`finance.andregoepel.dev`.

Adjust hostnames, paths and the container IP to your environment. The only
values the app itself cares about are the environment variables in the
Compose block below.

## What the app needs

| Key | Purpose |
|---|---|
| `ConnectionStrings__financeapp-database` | Postgres connection string. The name is `financeapp-database`, set in `Program.cs` via `AppFoundationOptions.DatabaseConnectionName`. |
| `DataProtection__CertificatePath` | Path to the mounted PFX that encrypts the key ring (see [data-protection.md](data-protection.md)). Required in Production; without it the app refuses to start. |
| `DataProtection__CertificatePassword` | PFX password. |
| `AppFoundation__KnownProxyNetworks` | CIDR of the Docker network the proxy lives on, so `X-Forwarded-*` headers are trusted. |
| `EmailSender__*` | Optional. SMTP settings for identity mails (password reset, confirmation). Without them those flows don't work; the app starts fine. Keys: `SenderName`, `SenderEmail`, `Server`, `Port`, `UseSsl`, `Username`, `Password`. |

Provider secrets (Wise token, Enable Banking credentials, Claude API key) are
**not** configured through the environment. They are entered in the UI after
setup and stored encrypted as `ProviderCredential` documents.

The container listens on port `8080` and runs as the non-root user `1654`.

## One-time preparation

### 1. Create the database

The app does not create its database. On the shared Postgres container:

```bash
docker exec postgres psql -U postgres -c "CREATE DATABASE financeapp;"
```

Schema objects (Marten tables, key ring, identity) are created by the app on
first start.

### 2. Generate the DataProtection certificate

A self-signed certificate is correct here. It only encrypts the key ring in
the database and is never presented to a browser. It has nothing to do with
the TLS certificate nginx uses.

```bash
mkdir -p /opt/nerdventures/certs/finance.andregoepel.dev && cd /opt/nerdventures/certs/finance.andregoepel.dev && openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -subj "/CN=FinanceApp DataProtection" -keyout dp.key -out dp.crt -noenc
```

Pack key and certificate into a password-protected PFX. The password you
enter at the prompt becomes `FINANCE_CERTIFICATE_PASSWORD` below:

```bash
cd /opt/nerdventures/certs/finance.andregoepel.dev && openssl pkcs12 -export -out dp_financeapp.pfx -inkey dp.key -in dp.crt
```

Remove the intermediate files and make the PFX readable for the container
user. World-readable is fine: the PFX is password-protected, so read access
alone does not expose the private key. Without this step UID 1654 cannot open
the file and the app crashes at startup.

```bash
cd /opt/nerdventures/certs/finance.andregoepel.dev && rm dp.key dp.crt && chmod 0444 dp_financeapp.pfx
```

Older `openssl` versions don't know `-noenc`; use `-nodes` instead.

**Back up the PFX and its password now, separately from database backups.**
If the file is lost, every stored provider credential is unrecoverable. See
[data-protection.md](data-protection.md) for backup and rotation details.

### 3. Add the secret to `.env`

Next to the `docker-compose.yml`:

```bash
echo 'FINANCE_CERTIFICATE_PASSWORD=<password>' >> .env
```

After the approved `main` build has finished, copy the
`Published ghcr.io/andregoepel/finance-app@sha256:…` reference from its
**Record immutable image digest** step and add it as the deployment's explicit
image identity:

```dotenv
FINANCE_APP_IMAGE=ghcr.io/andregoepel/finance-app@sha256:<approved-main-build-digest>
```

Changing this line is the production deployment decision. Never set it to
`latest`, a commit tag, or a semantic-version tag.

### 4. Registry access

If the GHCR package is private, the VPS needs a `docker login ghcr.io` with a
token that has `read:packages`.

## Compose service

Add to the existing `docker-compose.yml`. `APPFOUNDATION_DB_PASSWORD` is the
shared Postgres password already used by the other services; the IP must be
free on the `nerdventures` network (`172.28.0.0/16`).

```yaml
  finance.andregoepel.dev:
    image: ${FINANCE_APP_IMAGE:?Set FINANCE_APP_IMAGE to an immutable ghcr.io/andregoepel/finance-app@sha256 digest}
    container_name: finance-andregoepel-dev
    restart: unless-stopped
    depends_on:
      - postgres
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - DataProtection__CertificatePath=/keys/dp_financeapp.pfx
      - DataProtection__CertificatePassword=${FINANCE_CERTIFICATE_PASSWORD}
      - AppFoundation__KnownProxyNetworks=172.28.0.0/16
      - ConnectionStrings__financeapp-database=Host=postgres;Port=5432;Database=financeapp;Username=postgres;Password=${APPFOUNDATION_DB_PASSWORD}
    volumes:
      - /opt/nerdventures/certs/finance.andregoepel.dev/dp_financeapp.pfx:/keys/dp_financeapp.pfx:ro
    networks:
      nerdventures:
        ipv4_address: 172.28.0.50
```

## nginx

Add an upstream and a server block to `nginx.conf`. Position relative to a
`*.andregoepel.dev` wildcard block does not matter: nginx prefers the exact
`server_name`. The wildcard TLS certificate for `andregoepel.dev` covers the
subdomain.

```nginx
    upstream finance_andregoepel_dev_upstream {
        server 172.28.0.50:8080;
    }

    server {
        listen 443 ssl http2;
        server_name finance.andregoepel.dev;

        ssl_certificate     /etc/nginx/certs/andregoepel.dev/fullchain.pem;
        ssl_certificate_key /etc/nginx/certs/andregoepel.dev/privkey.pem;
        ssl_protocols       TLSv1.2 TLSv1.3;

        client_max_body_size 50m;   # CSV statement uploads

        location / {
            proxy_pass         http://finance_andregoepel_dev_upstream;
            proxy_http_version 1.1;

            # Blazor Server circuit (WebSocket)
            proxy_set_header   Upgrade $http_upgrade;
            proxy_set_header   Connection $connection_upgrade;

            proxy_set_header   Host              $host;
            proxy_set_header   X-Real-IP         $remote_addr;
            proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
            proxy_set_header   X-Forwarded-Proto $scheme;

            proxy_read_timeout  300s;
            proxy_send_timeout  300s;
            proxy_buffering     off;
        }
    }
```

`$connection_upgrade` comes from the usual `map $http_upgrade
$connection_upgrade { default upgrade; '' close; }` in the `http` block. An
HTTP-to-HTTPS redirect for the hostname is expected to exist already (a
`server_name *.andregoepel.dev` block on port 80 covers it).

## First start

```bash
export FINANCE_APP_IMAGE=ghcr.io/andregoepel/finance-app@sha256:<approved-main-build-digest>
docker compose pull finance.andregoepel.dev && docker compose up -d finance.andregoepel.dev && docker compose exec proxy nginx -s reload
```

Verify that the running container uses the selected local image object:

```bash
expected_image_id=$(docker image inspect --format '{{.Id}}' "$FINANCE_APP_IMAGE")
actual_image_id=$(docker inspect --format '{{.Image}}' finance-andregoepel-dev)
test "$actual_image_id" = "$expected_image_id"
actual_repo_digests=$(docker image inspect --format '{{join .RepoDigests "\n"}}' "$actual_image_id")
printf '%s\n' "$actual_repo_digests" | grep -Fx "$FINANCE_APP_IMAGE"
```

Record the selected manifest digest and actual image ID in the deployment log.

Then:

1. Open `https://finance.andregoepel.dev/Setup` and create the administrator
   account.
2. Under connections, store the Wise personal token (and later the Enable
   Banking and Claude credentials). Enable Banking needs the public callback
   URL registered as redirect URI in its portal.
3. Check that the proxy is trusted: the page must render styled and
   interactive. If it comes up unstyled, verify with

   ```bash
   curl -sk https://finance.andregoepel.dev/ | grep -o '<base href="[^"]*"'
   ```

   The result must be `https://…`. If it is `http://`,
   `AppFoundation__KnownProxyNetworks` does not cover the proxy's source
   address.

## Updating

Wait for the chosen `main` build to succeed, then export its digest-qualified
reference and put the same value in the VPS `.env`. Apply and verify it:

```bash
export FINANCE_APP_IMAGE=ghcr.io/andregoepel/finance-app@sha256:<new-approved-main-build-digest>
# Replace the FINANCE_APP_IMAGE line in .env with this exact value before continuing.
docker compose pull finance.andregoepel.dev && docker compose up -d finance.andregoepel.dev
expected_image_id=$(docker image inspect --format '{{.Id}}' "$FINANCE_APP_IMAGE")
actual_image_id=$(docker inspect --format '{{.Image}}' finance-andregoepel-dev)
test "$actual_image_id" = "$expected_image_id"
actual_repo_digests=$(docker image inspect --format '{{join .RepoDigests "\n"}}' "$actual_image_id")
printf '%s\n' "$actual_repo_digests" | grep -Fx "$FINANCE_APP_IMAGE"
```

Only pushes to `main` publish images. A moving tag may help locate a build, but
it is never the production identity.

## Rolling back

Export the previous digest-qualified reference, restore the identical
`FINANCE_APP_IMAGE=...@sha256:...` line in `.env`, run the same pull/up commands,
and repeat the image-ID and `RepoDigests` verification. Preserve every
successfully deployed digest so rollback never depends on where a mutable tag
points later.

## Troubleshooting

| Symptom | Cause |
|---|---|
| Startup throws `The DataProtection key ring would be stored unencrypted` | `DataProtection__CertificatePath` not set or empty in Production. |
| Startup throws `UnauthorizedAccessException: Access to the path '/keys/dp_financeapp.pfx' is denied` | PFX not readable by UID 1654, run `chmod 0444` on the host file. |
| `database "financeapp" does not exist` | Step 1 skipped. |
| Page loads unstyled, no interactivity | Proxy not trusted, see "First start" step 3. |
| Password reset mails never arrive | `EmailSender__*` not configured. |
