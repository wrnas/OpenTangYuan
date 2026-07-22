[中文版](troubleshooting_zh-CN.md) · [Back to Documentation](README.md) · [Back to Project Home](../README.md)

# Troubleshooting

When diagnosing an issue, first confirm the OpenTangYuan version, startup method, port, and configuration file in use. Visual Studio, the command line, and Docker may use different addresses or working directories.

## 1. The Service Does Not Start

Check:

- whether .NET 8 is installed;
- whether `dotnet restore TangYuan.sln` succeeds;
- whether the configured port is already in use;
- whether the SQLite path is valid;
- whether `appsettings.json` contains valid JSON;
- whether `skill-manifest.json` exists and is valid;
- whether the solution references the correct project path after moving the project under `src/`.

Recommended commands:

```bash
dotnet build TangYuan.sln -c Release
dotnet run --project src/OpenTangYuan --urls "http://localhost:54124"
```

## 2. Swagger Is Not Reachable

Check:

- the actual startup port;
- whether Swagger is enabled in the current environment;
- whether the service is using HTTP or HTTPS;
- whether the reverse-proxy path is correct;
- whether production settings disable or restrict Swagger.

## 3. Email Cannot Be Sent

Check:

- SMTP server and port;
- SSL/TLS settings;
- sender address and authorization code;
- whether the provider allows SMTP;
- whether an application-specific authorization code is required;
- whether the network or firewall blocks the connection;
- whether the recipient and attachment paths are valid.

## 4. Email Cannot Be Searched or Read

Check:

- IMAP server and port;
- whether IMAP is enabled in the email account settings;
- whether the authorization code is correct;
- whether the provider restricts third-party clients;
- whether `contextKey` matches the previous search;
- whether the requested `index` exists in the current search results.

## 5. Attachments Cannot Be Downloaded

Check:

- whether an email search was performed first;
- whether the selected message index is correct;
- whether `savePath` exists or can be created;
- whether the target directory is within the allowed scope;
- whether the runtime account has write permission;
- whether sufficient disk space is available.

## 6. A File Cannot Be Opened or Printed

Check:

- whether the file exists;
- whether the path is under `AllowedRoots`;
- whether the runtime account has permission;
- whether a default application is installed for the file type;
- whether the printer and print service are available;
- whether escaping and quotation marks in the path are correct.

## 7. File Search Returns No Results

Check:

- whether the keyword and extension are correct;
- whether Windows Search or the Everything service is available;
- whether the target directory has been indexed;
- whether the runtime account can access the directory;
- whether the file is within the allowlisted scope.

## 8. A Browser Task Fails

Check:

- whether Playwright and its browser assets are fully installed;
- whether the target site requires authentication;
- whether CSS selectors are still valid;
- whether CAPTCHA, multi-factor authentication, or anti-automation controls block the task;
- whether the browser session has already been closed;
- whether the download directory is writable.

## 9. Screenshot Capture Fails

Check:

- whether the runtime is running in a Windows session with desktop access;
- whether the service account is a background account without desktop access;
- whether the local application has already been opened;
- whether a web-page screenshot was incorrectly sent to a desktop screenshot skill;
- whether the output directory exists and is writable.

## 10. A Later Step Cannot Reference an Earlier Result

Check:

- whether the step number is correct, such as `step0` or `step1`;
- whether the previous step succeeded;
- whether the field path matches the actual response;
- whether the correct letter case is used;
- whether the template uses the form `{{step0.data.path}}`;
- whether the caller guessed a response field that does not exist.

Use debug output, when available, to inspect the resolved arguments and result summary for each step.

## 11. A Skill Does Not Exist

Possible causes:

- the `SkillCode` is misspelled;
- the current deployment does not include the extension;
- `skill-manifest.json` failed to load;
- the specified workflow does not exist in the database;
- the agent is using a skill name from an older version.

Call `GetSkillListForAI` again instead of relying on a cached capability catalog.

## 12. A Side-Effect Operation Is Blocked

Possible causes:

- the request is a duplicate;
- the path is outside the allowlist;
- the executable is outside the allowlist;
- required parameters are missing;
- a security policy rejected the request;
- the current identity does not have permission;
- the operation requires human confirmation.

Do not attempt to bypass the policy. Adjust the configuration or request administrator approval based on the returned error.

## 13. Desktop Features Do Not Work in Docker

This is an expected limitation. Docker is primarily suitable for API and server-side validation and normally does not have access to the interactive Windows desktop.

Run the task in the Windows local runtime when it requires screenshots, document opening, or desktop application invocation.

## 14. Git or Visual Studio Fails After Moving Directories

Check:

- whether the `.sln` file remains at the repository root;
- whether the `.csproj` has been added from the new `src/OpenTangYuan/` location;
- whether Visual Studio should be closed and the local `.vs/` cache removed;
- whether `git status` works from the command line;
- whether `.gitignore` excludes `.vs/`, `bin/`, and `obj/`;
- whether old paths in the Dockerfile, README, or build scripts have been updated.

## 15. Collecting Diagnostic Information

When reporting an issue, provide:

- the OpenTangYuan release or commit;
- operating system and .NET version;
- startup method;
- API and `SkillCode` involved;
- redacted request parameters;
- HTTP status code and `errorCode`;
- redacted logs;
- whether the issue can be reproduced through Swagger.

Do not publish email authorization codes, API keys, real internal paths, private email content, or sensitive screenshots in an issue.
