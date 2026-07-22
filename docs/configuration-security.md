[中文版](configuration-security_zh-CN.md) · [Back to Documentation](README.md) · [Back to Project Home](../README.md)

# Configuration and Security Controls

OpenTangYuan can access email, file systems, browsers, desktop applications, and enterprise systems. Deployments should follow the principle of least privilege and keep development and production configuration separate.

## 1. Sensitive Information Management

Do not commit the following information to Git:

- email passwords or authorization codes;
- API keys;
- enterprise messaging webhook keys;
- database passwords;
- internal-system tokens;
- real internal network addresses;
- private email, file paths, screenshots, or runtime logs;
- SQLite databases containing sensitive data.

Recommended storage mechanisms include:

- environment variables;
- .NET User Secrets;
- Docker Secrets;
- CI/CD secret variables;
- separate production configuration files;
- operating-system or enterprise secret-management services.

Example configuration should contain placeholders only.

## 2. Email Configuration

Example:

```json
{
  "EmailSettings": {
    "SmtpServer": "smtp.example.com",
    "SmtpPort": 465,
    "SmtpUseSsl": true,
    "SenderEmail": "your-email@example.com",
    "SenderPassword": "your-authorization-code",
    "ImapServer": "imap.example.com",
    "ImapPort": 993,
    "ImapUseSsl": true
  }
}
```

Before configuration, confirm that the email provider has enabled SMTP, IMAP, and third-party client access. Many providers require an application-specific authorization code rather than the account login password.

## 3. File Access Allowlist

Example:

```json
{
  "FileSystem": {
    "AllowedRoots": [
      "C:\\Users\\Public\\Documents",
      "D:\\Work",
      "D:\\Temp"
    ]
  }
}
```

Recommendations:

- allow only directories required for the intended workflow;
- do not allow an entire system drive by default;
- evaluate permissions for network shares separately;
- consider different policies for read-only and write operations;
- normalize paths supplied by users or agents to prevent directory traversal or out-of-scope access.

## 4. Executable Allowlist

Example:

```json
{
  "AllowedExeNames": [
    "pandoc.exe",
    "custom-tool.exe"
  ]
}
```

Recommendations:

- allow only explicitly required applications;
- do not allow general command interpreters or arbitrary script runners unless the deployment has additional isolation;
- record the executable path, arguments, execution time, and result;
- enforce limits for timeouts, exit codes, and output size.

## 5. Common Configuration Keys

| Key | Requirement | Description |
|---|---|---|
| `EmailSettings:SmtpServer` | As needed | SMTP server. |
| `EmailSettings:SmtpPort` | As needed | SMTP port. |
| `EmailSettings:SenderEmail` | As needed | Sender address. |
| `EmailSettings:SenderPassword` | As needed | Email authorization code. |
| `EmailSettings:ImapServer` | As needed | IMAP server. |
| `ConnectionStrings:Sqlite` | Required | SQLite connection string for workflows and related data. |
| `FileSystem:AllowedRoots` | Strongly recommended | Directory scope accessible to the runtime. |
| `AllowedExeNames` | Strongly recommended | Executables that the runtime may launch. |
| `DebugMode` | Optional | Enables more detailed debug responses or logging. |

The exact configuration keys should be verified against the current `appsettings.json` and implementation.

## 6. API Access Control

Development and production environments have different security requirements.

A production deployment should use one or more of the following protections:

- HTTPS;
- API keys;
- reverse-proxy authentication;
- VPN access;
- IP allowlists;
- internal gateways;
- user or service identity authentication.

Do not assume that local development settings automatically enable all production-grade controls. Operators should review controller authorization, gateway rules, and Swagger access policies.

## 7. Swagger

Swagger is useful for development and API debugging. In production, consider one of the following:

- disable Swagger UI;
- protect Swagger with authentication;
- restrict Swagger to an administrative network.

## 8. Side-Effect Operations

The following operations require additional control:

- sending or replying to email;
- downloading attachments;
- copying, moving, renaming, or deleting files;
- printing;
- launching applications;
- sending messages to enterprise platforms;
- calling write operations in internal systems.

Recommended controls:

1. argument validation;
2. path and executable allowlists;
3. request idempotency or deduplication;
4. stop-after-success rules;
5. human confirmation for high-risk operations;
6. execution and audit logging;
7. limited retries after failure.

## 9. Logging and Privacy

Logs should contain enough information for diagnostics without recording sensitive content unnecessarily.

Recommended fields:

- request time;
- invoked `SkillCode`;
- execution mode;
- success or failure status;
- error code;
- elapsed time;
- redacted target information.

Avoid recording:

- email passwords or authorization codes;
- complete email bodies;
- complete API tokens;
- unredacted internal file paths;
- private user data;
- unnecessary screenshot content.

## 10. Production Deployment Checklist

- [ ] HTTPS or a secure gateway is enabled.
- [ ] The runtime is not exposed directly to the public internet.
- [ ] Authentication and source restrictions are configured.
- [ ] Swagger is disabled or protected.
- [ ] Email and webhook credentials are not stored in the repository.
- [ ] File path allowlists are narrowly scoped.
- [ ] Executable allowlists are narrowly scoped.
- [ ] High-risk side effects require confirmation or approval.
- [ ] Critical operations are logged.
- [ ] Logs and databases do not retain unnecessary sensitive data.
- [ ] Backup, recovery, and credential-rotation procedures have been tested.

## 11. Related Documentation

- [Deployment and Platform Support](deployment-platform.md)
- [Agent Integration Guide](agent-integration.md)
- [Troubleshooting](troubleshooting.md)
