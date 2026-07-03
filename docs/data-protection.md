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

Without a configured certificate (local development) keys are stored
unencrypted and ASP.NET Core logs its "keys not encrypted at rest" warning.

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
