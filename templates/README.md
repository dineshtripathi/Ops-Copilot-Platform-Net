# Templates

This directory provides **starter templates** for common OpsCopilot
automation tasks. Copy a template into your own repository and customise
it — these files are intentionally minimal so you can extend them.

> **Note:** Templates live here (`templates/`) and are *not* active
> workflows. The project's real CI/CD lives in
> `infrastructure/pipelines/deploy-infra.yml`.

## Contents

| Path | Description |
| ---- | ----------- |
| [github-actions/ci.yml](github-actions/ci.yml) | Build + test GitHub Actions workflow for OpsCopilot |
| [github-actions/deploy-infra.yml](github-actions/deploy-infra.yml) | Bicep infrastructure deployment with OIDC auth |
| [bicep/tenant-skeleton.bicep](bicep/tenant-skeleton.bicep) | Blank subscription-scope Bicep for a new tenant |

## Usage

```bash
# Copy the CI template into your fork
cp templates/github-actions/ci.yml .github/workflows/ci.yml

# Copy the infra deployment template
cp templates/github-actions/deploy-infra.yml .github/workflows/deploy-infra.yml
```

After copying, replace placeholder values (marked `<!-- TODO -->` or
`<PLACEHOLDER>`) with your own configuration.

## License

MIT — see [LICENSE](../LICENSE)
