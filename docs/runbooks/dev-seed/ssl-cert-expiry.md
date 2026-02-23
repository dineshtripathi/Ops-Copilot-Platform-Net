# SSL/TLS Certificate Expiry
tags: ssl, tls, certificate, expiry, https, security

## Symptoms
- Browser shows certificate warning or ERR_CERT_DATE_INVALID
- API clients failing with SSL handshake errors
- Monitoring alerts for certificate expiry within 30/14/7 days
- Automated certificate renewal failures

## Diagnosis Steps

1. **Check certificate expiry**: `openssl s_client -connect host:443 </dev/null 2>/dev/null | openssl x509 -noout -dates`
2. **Verify certificate chain**: Ensure intermediate certificates are included.
3. **Check renewal automation**: Review cert-manager, Let's Encrypt, or Key Vault renewal status.
4. **Review DNS**: Verify the domain points to the correct endpoint.

## KQL Queries

```kql
// Find certificate-related errors
AppServiceHTTPLogs
| where ScStatus >= 400
| where CsSslProtocol != ""
| summarize ErrorCount = count() by CsHost, CsSslProtocol, ScStatus, bin(TimeGenerated, 1h)
| order by ErrorCount desc
```

## Remediation

1. **Renew the certificate manually** if automated renewal failed:
   - Let's Encrypt: `certbot renew --force-renewal`
   - Azure Key Vault: Trigger manual renewal in the portal
   - cert-manager: Delete the Certificate resource to trigger re-issuance

2. **Update the certificate** on the hosting platform:
   - App Service: Upload via TLS/SSL settings
   - Application Gateway: Update the listener certificate
   - Kubernetes: Update the TLS secret

3. **Verify the fix**: `curl -vI https://your-domain.com` to confirm the new certificate.

## Prevention
- Set up monitoring alerts at 30, 14, and 7 days before expiry
- Use automated certificate management (cert-manager, Key Vault)
- Document certificate inventory with owners and renewal procedures
- Test certificate renewal in staging before production
