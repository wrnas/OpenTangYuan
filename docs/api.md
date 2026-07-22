[中文版](api_zh-CN.md) · [Back to Documentation](README.md) · [Back to Project Home](../README.md)

# OpenTangYuan Core API Reference

This document covers the capability-discovery and workflow-execution APIs most commonly used by agents and external clients. For skill-specific parameters and examples, see the [Built-in Skills Reference](builtin-skills.md).

## 1. API Call Model

The core OpenTangYuan API flow is:

```text
GetSkillListForAI
        ↓
GetBuiltinSkillDetail / GetSkillAction
        ↓
ExecuteSkill / ExecuteSkillForCoze
        ↓
Trusted local runtime
        ↓
Structured execution result
```

Recommended principles:

1. query the capability catalog first;
2. prefer a matching database workflow when one exists;
3. query built-in skill details only when no suitable workflow is available;
4. ask the user for required parameters instead of guessing paths, email addresses, or credentials;
5. stop immediately after a side-effect operation succeeds;
6. retry a failed call at most once after correcting the parameters.

## 2. Basic Information

### Base URL

Example local address:

```text
http://localhost:54124
```

The port may differ when OpenTangYuan is started through Visual Studio or another launch profile. Use the startup log and Swagger page as the source of truth.

### Content-Type

POST requests use:

```http
Content-Type: application/json
```

### Authentication

Production deployments should protect the local runtime through an API key, gateway, VPN, IP allowlist, or another access-control mechanism. The exact authentication header and enablement method depend on the deployed configuration; do not assume that a development environment includes complete production authentication by default.

### Swagger

```text
http://localhost:54124/swagger
```

## 3. Core Endpoint Overview

| API | Method | Endpoint | Purpose |
|---|---|---|---|
| `GetSkillListForAI` | POST | `/api/Skills/GetSkillListForAI` | Retrieve summaries of available workflows and built-in skills. |
| `GetBuiltinSkillDetail` | POST | `/api/Skills/GetBuiltinSkillDetail` | Retrieve the detailed definition of one built-in skill. |
| `GetBuiltinSkillManifest` | POST | `/api/Skills/GetBuiltinSkillManifest` | Retrieve the complete skill manifest, primarily for debugging or documentation generation. |
| `GetSkillAction` | POST | `/api/Skills/GetSkillAction` | Retrieve the step definition of a database workflow. |
| `ExecuteSkill` | POST | `/api/Skills/ExecuteSkill` | Execute a built-in skill, database workflow, or temporary workflow. |
| `ExecuteSkillForCoze` | POST | `/api/Skills/ExecuteSkillForCoze` | Accept serialized string parameters for agent platforms that cannot conveniently send complex JSON. |

## 4. Retrieve the Capability Catalog

### `GetSkillListForAI`

```http
POST /api/Skills/GetSkillListForAI
```

The request body may be an empty object:

```json
{}
```

Example response:

```json
{
  "success": true,
  "data": {
    "workflows": [
      {
        "skillCode": "capture_and_send_email",
        "AIDesc": "Capture a screenshot and send it by email",
        "sourceType": "workflow",
        "needDetail": true
      }
    ],
    "builtins": [
      {
        "skillCode": "email_task",
        "AIDesc": "Email operations including search, read, send, and attachment download",
        "sourceType": "builtin",
        "needDetail": true
      }
    ]
  }
}
```

Field descriptions:

| Field | Description |
|---|---|
| `workflows` | Reusable workflows stored in the database. |
| `builtins` | Built-in skills registered in the skill manifest. |
| `skillCode` | Skill or workflow identifier. |
| `AIDesc` | Capability summary intended for agents. |
| `sourceType` | Either `workflow` or `builtin`. |
| `needDetail` | Indicates whether the caller should retrieve the detailed definition. |

## 5. Retrieve Built-in Skill Details

### `GetBuiltinSkillDetail`

```http
POST /api/Skills/GetBuiltinSkillDetail
```

Example request:

```json
{
  "skillCode": "email_task"
}
```

This endpoint returns the actions, parameters, constraints, and examples supported by a skill. Agents should generate arguments from the actual response instead of depending on a permanently hard-coded skill dictionary.

## 6. Retrieve the Complete Skill Manifest

### `GetBuiltinSkillManifest`

```http
POST /api/Skills/GetBuiltinSkillManifest
```

Request body:

```json
{}
```

This endpoint is useful for debugging, documentation generation, or one-time retrieval of the complete definition. Ordinary agent calls should normally retrieve a summary first and request the details of individual skills on demand to reduce context usage.

## 7. Retrieve a Database Workflow

### `GetSkillAction`

```http
POST /api/Skills/GetSkillAction
```

Example request:

```json
{
  "skillCode": "capture_and_send_email"
}
```

Example response:

```json
{
  "success": true,
  "data": {
    "skillCode": "capture_and_send_email",
    "remark": "Capture a screenshot and send it by email",
    "skillType": "workflow",
    "steps": [
      {
        "action": "screenshot_task",
        "args": {
          "action": "capture_full_screen"
        }
      },
      {
        "action": "email_task",
        "args": {
          "action": "send",
          "to": "someone@example.com",
          "subject": "Screen Capture",
          "body": "The screenshot is shown below.",
          "insertImagePaths": [
            "{{step0.data.path}}"
          ]
        }
      }
    ]
  }
}
```

An agent should inspect the workflow steps and parameter requirements before execution rather than invoking a workflow based only on its name.

## 8. Unified Execution Endpoint

### `ExecuteSkill`

```http
POST /api/Skills/ExecuteSkill
```

Top-level fields:

| Field | Type | Required | Description |
|---|---|---|---|
| `SkillCode` | string | Yes | Skill or workflow identifier. |
| `Arguments` | object | No | Arguments for a single skill or workflow input. Pass `{}` when no arguments are required. |
| `Steps` | array | No | Steps for a temporary workflow. |

### Execute a Built-in Skill

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

### Execute a Database Workflow

```json
{
  "SkillCode": "capture_and_send_email",
  "Arguments": {}
}
```

### Execute a Temporary Workflow

```json
{
  "SkillCode": "temp_task",
  "Arguments": {},
  "Steps": [
    {
      "Action": "screenshot_task",
      "Args": {
        "action": "capture_full_screen"
      }
    },
    {
      "Action": "email_task",
      "Args": {
        "action": "send",
        "to": "someone@example.com",
        "subject": "Screen Capture",
        "body": "The automatically captured screenshot is shown below.",
        "insertImagePaths": [
          "{{step0.data.path}}"
        ]
      }
    }
  ]
}
```

The execution mode is determined by the request and whether a database workflow with the same identifier exists:

| Mode | Description |
|---|---|
| `builtin` | Execute a built-in skill. |
| `workflow` | Execute a database workflow. |
| `temp_workflow` | Execute the temporary workflow supplied in the request. |

## 9. Coze-Compatible Execution Endpoint

### `ExecuteSkillForCoze`

```http
POST /api/Skills/ExecuteSkillForCoze
```

The request body contains one string field:

```json
{
  "Json": "{\"skillCode\":\"email_task\",\"arguments\":{\"action\":\"search\",\"subjectKeyword\":\"notification\",\"maxCount\":10}}"
}
```

After deserialization, the content is:

```json
{
  "skillCode": "email_task",
  "arguments": {
    "action": "search",
    "subjectKeyword": "notification",
    "maxCount": 10
  }
}
```

This endpoint is intended for platforms that can only pass string parameters or cannot conveniently represent deeply nested JSON objects.

## 10. Workflow Steps and Context

Each workflow step uses this structure:

```json
{
  "Action": "email_task",
  "Args": {
    "action": "send",
    "to": "someone@example.com",
    "subject": "Test",
    "body": "Hello"
  }
}
```

Execution results are stored in sequence as:

```text
step0
step1
step2
...
```

Later steps can reference values such as:

```text
{{step0.data.path}}
{{step0.data.firstPath}}
{{step1.result}}
```

## 11. Responses and Error Handling

A successful response commonly contains:

```json
{
  "success": true,
  "message": "Execution succeeded",
  "data": {}
}
```

A failed response commonly contains:

```json
{
  "success": false,
  "message": "Invalid arguments",
  "errorCode": "INVALID_ARGUMENTS",
  "data": null
}
```

Common error types include:

| Error code | Description |
|---|---|
| `SKILL_NOT_FOUND` | The requested skill does not exist. |
| `INVALID_ARGUMENTS` | One or more arguments are invalid. |
| `MISSING_ARGUMENTS` | Required arguments are missing. |
| `EMAIL_CONFIG_MISSING` | Email configuration is incomplete. |
| `FILE_NOT_FOUND` | The target file does not exist. |
| `EXECUTION_FAILED` | Skill execution failed. |
| `WORKFLOW_EXECUTION_FAILED` | Workflow execution failed. |
| `SIDE_EFFECT_BLOCKED` | A side-effect operation was blocked. |
| `PERMISSION_DENIED` | The caller does not have sufficient permission. |
| `FORBIDDEN` | A security policy rejected the operation. |
| `TIMEOUT` | The operation timed out. |
| `INTERNAL_ERROR` | An internal server error occurred. |

The exact HTTP status codes and response fields should be verified against Swagger and the actual response of the current release.

## 12. Management and Browser Endpoints

The project also provides workflow-management endpoints and independent browser-session endpoints, including:

```text
POST /api/Skills/SaveSkillAction
POST /api/Skills/GetSkillList
POST /api/Skills/DeleteSkill
POST /api/Skills/GetAllSkillCodes

POST /AiApi/Browser/start
POST /AiApi/Browser/run
POST /AiApi/Browser/close
GET  /AiApi/Browser/sessions
```

These endpoints are primarily intended for administrative interfaces, debugging tools, or specialized clients. Confirm the current request schema through Swagger before using them.

## 13. Related Documentation

- [Built-in Skills Reference](builtin-skills.md)
- [Agent Integration Guide](agent-integration.md)
- [Configuration and Security Controls](configuration-security.md)
- [Troubleshooting](troubleshooting.md)
