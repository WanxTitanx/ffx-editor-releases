# Reporting security issues

Do not put credentials, private saves or game dumps in public issues. Use GitHub's
private vulnerability reporting for a reproducible security issue when available.

Builds and ordinary local editing require no shared API credentials. Optional AI
credentials belong in the user's local configuration, never in source files.

Before publishing changes, run Gitleaks against the Git history:

```sh
gitleaks git . --config .gitleaks.toml --redact
```

The configuration extends the default rules and allows only five exact,
path-specific false positives: three intentionally fake test values, one JDK
algorithm name and one minified parser expression. It does not exclude entire
application or test directories. Review new candidates instead of broadly
disabling rules. A passing scanner is one check, not a guarantee of no secrets.
