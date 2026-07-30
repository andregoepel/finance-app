# DataProtection key ring

Since `AndreGoepel.AppFoundation.Hosting` 1.1.0 the DataProtection key ring
persistence and encryption come from the foundation
([app-foundation#40](https://github.com/andregoepel/app-foundation/issues/40)) —
finance-app carries no local implementation. `AddAppFoundation()` persists the
key ring in Postgres as Marten documents (table
`mt_doc_dataprotectionkeydocument`), so it survives container rebuilds. Provider
credentials (`ProviderCredential`, Phase 3) are encrypted with these keys —
**a lost key ring makes all stored credentials unrecoverable**.

## Encryption at rest

In production the key ring entries are additionally encrypted with an X.509
certificate before they are written to the database. The certificate's private
key lives only on the app host (mounted secret), never in the database — a
database dump alone cannot decrypt stored credentials. App and database run on
separate hosts in production, so the two secrets never sit together.

Configuration (via the key-per-file secrets directory `/run/secrets` or
environment variables):

| Key | Purpose |
|---|---|
| `DataProtection__CertificatePath` | Path to the mounted PFX file |
| `DataProtection__CertificatePassword` | PFX password |

### Startup guard

Since `AndreGoepel.AppFoundation.Hosting` 1.2.x, a missing certificate is no
longer a warning — the app **fails to start**. If no certificate is
configured and the escape hatch below isn't set, startup throws:

```
System.InvalidOperationException: The DataProtection key ring would be stored unencrypted in the database … Configure key encryption — set DataProtection:CertificatePath, or use AppFoundationOptions.ConfigureDataProtection … or set AppFoundationOptions.AllowUnprotectedKeyRing = true …
```

- **Local development** — the guard is relaxed automatically, so the app
  starts without a certificate configured (keys are stored unencrypted, as
  before).
- **Production** — the guard is strictly enforced: the app refuses to start
  unless either a certificate is configured or the escape hatch below is set.

Escape hatch, intended for databases that are already encrypted at rest by
the hosting platform (e.g. managed Postgres with disk-level encryption) and
therefore don't need a second layer of encryption from the key ring itself:

```csharp
builder.AddAppFoundation(options =>
{
    // ...
    options.AllowUnprotectedKeyRing = true;
});
```

Set this deliberately, not as a stand-in for "I haven't configured a
certificate yet" — it disables encryption at rest for the key ring described
above, for the lifetime of the setting.

## Generating the certificate

```bash
openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 \
  -subj "/CN=FinanceApp DataProtection" \
  -keyout dp.key -out dp.crt -noenc
openssl pkcs12 -export -out financeapp-dataprotection.pfx \
  -inkey dp.key -in dp.crt
rm dp.key dp.crt
```

Certificate expiry only stops *new* keys from being encrypted; decryption of
existing keys keeps working after expiry.

### Non-root containers and PFX permissions

The app image runs as a non-root user (UID `1654`). A PFX generated with a
strict umask (e.g. `openssl` run under `umask 077`) and then bind-mounted
into the container typically comes out `0600` and owned by `root` (or
whichever UID created it on the host) — the app user can't read it, and
startup crashes:

```
System.Security.Cryptography.CryptographicException: ...
 ---> System.UnauthorizedAccessException: Access to the path '/…/dp.pfx' is denied.
```

Fix one of:

- `chmod 0444 dp.pfx` on the mounted file. Safe even though it's now
  world-readable: the PFX is password-protected
  (`DataProtection__CertificatePassword`), so read access alone doesn't
  expose the private key.
- `chown 1654 dp.pfx` (or the equivalent on your deployment platform) so the
  app user owns the file.
- Mount the PFX as a Docker secret instead of a bind mount — secrets are
  mounted `0444` by default and are readable by the app user with no manual
  permission fix-up.

## Reverse proxy / forwarded headers

Behind a TLS-terminating reverse proxy (nginx, Traefik, etc.) the app itself
only ever sees plain HTTP from the proxy. Unless it's told to trust the
proxy, it renders as if the request came in over HTTP — most visibly, it
emits `<base href="http://…">` on a page the browser loaded over HTTPS. The
browser blocks the resulting relative CSS/JS requests as mixed content, so
the app renders unstyled and non-interactive.

### Trusting the proxy

By default the app trusts only loopback (`127.0.0.1` / `::1`) for
`X-Forwarded-*` headers. A reverse proxy running on a Docker network sits at
a different address, so its subnet needs to be added explicitly:

| Key | Purpose |
|---|---|
| `AppFoundation__KnownProxyNetworks` | Comma-separated CIDR ranges to trust for `X-Forwarded-*` headers |

```bash
AppFoundation__KnownProxyNetworks=172.20.0.0/16
```

Without this, `X-Forwarded-Proto` is ignored and the app falls back to the
scheme of the connection it actually received — HTTP from the proxy — even
though the client used HTTPS.

Find the Docker network's subnet:

```bash
docker network inspect <net> -f '{{range .IPAM.Config}}{{.Subnet}}{{end}}'
```

### nginx sample

```nginx
location / {
    proxy_pass http://financeapp:8080;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header X-Forwarded-Host  $host;
    proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
}
```

### Verifying

```bash
curl -sk https://your-host/ | grep -o '<base href="[^"]*"'
# expect: <base href="https://your-host/">
```

If it still shows `http://`, double check that
`AppFoundation__KnownProxyNetworks` covers the proxy's actual source address
(not just the subnet you expect it to use) and that the proxy is setting all
three `X-Forwarded-*` headers above.

## Backup

Back up the PFX (and its password) **separately from database backups** — it is
exactly as critical as the key ring itself:

- database backup alone → key ring unreadable → credentials lost
- PFX alone → nothing to decrypt

## Rotation

To rotate the certificate, configure the new PFX and keep the old one available
for decrypting existing keys via the foundation's extension point in
`Program.cs`:

```csharp
builder.AddAppFoundation(options =>
{
    // ...
    options.ConfigureDataProtection = dataProtection =>
        dataProtection.UnprotectKeysWithAnyCertificate(oldCertificate, newCertificate);
});
```
