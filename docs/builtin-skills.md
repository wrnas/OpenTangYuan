[中文版](builtin-skills_zh-CN.md) · [Back to Documentation](README.md) · [Back to Project Home](../README.md)

# Built-in Skills Reference

OpenTangYuan exposes local capabilities as built-in skills. A skill is executed through `ExecuteSkill`, while its current actions, parameters, examples, constraints, and side effects can be retrieved through `GetBuiltinSkillDetail`.

The examples below describe the common calling model. The deployed `skill-manifest.json`, Swagger definition, and actual API response are authoritative for the current version.

## 1. General Request Format

```json
{
  "SkillCode": "file_task",
  "Arguments": {
    "action": "search"
  }
}
```

For temporary multi-step workflows, place each built-in skill in a `Steps` array:

```json
{
  "SkillCode": "temp_task",
  "Arguments": {},
  "Steps": [
    {
      "Action": "file_task",
      "Args": {
        "action": "search",
        "keyword": "report",
        "ext": "docx"
      }
    }
  ]
}
```

## 2. `email_task`

Common email operations include:

| Action | Purpose |
|---|---|
| `send` | Send a new message. |
| `search` | Search messages and return a list. |
| `read` | Read the content of a selected message. |
| `download_attachments` | Download attachments from a selected message. |
| `reply` | Reply to a selected message. |
| `mark_read` | Mark a selected message as read. |
| `save_eml` | Save a selected message as an `.eml` file. |

### Search Email

```json
{
  "SkillCode": "email_task",
  "Arguments": {
    "action": "search",
    "subjectKeyword": "notification",
    "fromKeyword": "",
    "bodyKeyword": "",
    "unreadOnly": false,
    "hasAttachments": false,
    "maxCount": 10,
    "scanCount": 100,
    "contextKey": "mail_default"
  }
}
```

After returning a list, present it to the user and stop. Read or modify a specific message only after the user identifies it.

### Read Email

```json
{
  "SkillCode": "email_task",
  "Arguments": {
    "action": "read",
    "index": 1,
    "contextKey": "mail_default"
  }
}
```

### Download Attachments

```json
{
  "SkillCode": "email_task",
  "Arguments": {
    "action": "download_attachments",
    "index": 1,
    "contextKey": "mail_default",
    "savePath": "D:\\MailDownloads"
  }
}
```

### Send Email

```json
{
  "SkillCode": "email_task",
  "Arguments": {
    "action": "send",
    "to": "someone@example.com",
    "subject": "Test Message",
    "body": "This message was sent by OpenTangYuan.",
    "attachments": [
      "D:\\Files\\report.docx"
    ],
    "insertImagePaths": [
      "D:\\Images\\screen.png"
    ]
  }
}
```

Use `attachments` for ordinary attachments and `insertImagePaths` for images embedded in the message body. Stop after a successful send or reply operation.

## 3. `file_task`

Common file operations include:

| Action | Purpose |
|---|---|
| `search` | Search for files. |
| `copy` | Copy one file. |
| `move` | Move one file. |
| `copy_many` | Copy multiple files. |
| `move_many` | Move multiple files. |
| `rename` | Rename a file or directory. |
| `mkdir` | Create a directory. |

### Search for a File

```json
{
  "SkillCode": "file_task",
  "Arguments": {
    "action": "search",
    "keyword": "report",
    "ext": "docx"
  }
}
```

### Copy a File

```json
{
  "SkillCode": "file_task",
  "Arguments": {
    "action": "copy",
    "from": "D:\\Source\\report.docx",
    "to": "D:\\Target\\report.docx"
  }
}
```

### Create a Directory

```json
{
  "SkillCode": "file_task",
  "Arguments": {
    "action": "mkdir",
    "from": "D:\\Target"
  }
}
```

All file paths should be validated and restricted to the configured allowlist. File-changing operations must stop after success.

## 4. `browser_task`

`browser_task` executes browser automation steps. Depending on the current manifest, actions can include:

- navigating to a URL;
- waiting for an element;
- clicking;
- entering text;
- extracting text or lists;
- taking a web-page screenshot;
- downloading files;
- maintaining or closing a browser session.

Example:

```json
{
  "SkillCode": "browser_task",
  "Arguments": {
    "actions": [
      {
        "type": "goto",
        "url": "https://example.com"
      },
      {
        "type": "wait_for",
        "selector": "body"
      },
      {
        "type": "get_text",
        "selector": "body"
      }
    ],
    "closeSession": false,
    "includeOutputs": false
  }
}
```

Use `browser_task` for web-page screenshots. Use `screenshot_task` for the local desktop or a local application window.

## 5. `wechat_task`

Common WeChat Work message actions include:

| Action | Purpose |
|---|---|
| `text` | Send a text message. |
| `markdown` | Send a Markdown message. |
| `card` | Send a card message. |

Example:

```json
{
  "SkillCode": "wechat_task",
  "Arguments": {
    "action": "text",
    "content": "The task is complete",
    "isAtAll": false
  }
}
```

A successful message delivery is a side effect and should not be repeated automatically.

## 6. `open_task`

Open a local file, directory, or application:

```json
{
  "SkillCode": "open_task",
  "Arguments": {
    "path": "D:\\Files\\report.docx"
  }
}
```

The target should exist, be within the permitted scope, and have an associated application when applicable.

## 7. `print_task`

Print a local file:

```json
{
  "SkillCode": "print_task",
  "Arguments": {
    "path": "D:\\Files\\report.docx"
  }
}
```

Printing is a side-effect operation. Confirm the file, printer availability, and user intent before execution.

## 8. `screenshot_task`

Capture the local desktop or active application window:

```json
{
  "SkillCode": "screenshot_task",
  "Arguments": {
    "action": "capture_full_screen"
  }
}
```

The exact actions and response fields should be retrieved through `GetBuiltinSkillDetail`. Desktop capture requires a Windows session with desktop access.

## 9. `tool_task`

Invoke an allowlisted local tool or executable:

```json
{
  "SkillCode": "tool_task",
  "Arguments": {
    "exePath": "D:\\Tools\\CustomTool.exe",
    "arguments": "--help",
    "timeout": 10
  }
}
```

Only explicitly approved programs should be allowed. Validate the executable path and arguments, enforce a timeout, and record the result.

## 10. `folder_task`

`folder_task` organizes files according to supported rules, such as grouping them by extension. Query `GetBuiltinSkillDetail` for the exact action names and arguments provided by the current release.

## 11. `lock_task`

`lock_task` locks the local workstation. It changes the user's desktop state and should only be invoked after clear user intent has been established.

## 12. Skill Discovery and Versioning

Do not assume that every deployment exposes the same skill set or parameter schema. Agents and clients should:

1. call `GetSkillListForAI` to discover the current catalog;
2. call `GetBuiltinSkillDetail` before constructing arguments;
3. use only actions and fields returned by the deployed runtime;
4. handle unavailable optional skills gracefully;
5. avoid caching skill definitions across incompatible releases.

## 13. Related Documentation

- [Core API Reference](api.md)
- [Architecture and Runtime Model](architecture.md)
- [Agent Integration Guide](agent-integration.md)
- [Configuration and Security Controls](configuration-security.md)
