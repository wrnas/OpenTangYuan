[返回文档导航](README_zh-CN.md) · [返回项目首页](../README_zh-CN.md)

# OpenTangYuan 核心 API 参考

本文档说明 Agent 和外部客户端最常使用的能力发现与工作流执行接口。内置技能的具体参数与示例见 [内置技能参考](builtin-skills_zh-CN.md)。

## 1. API 调用模型

OpenTangYuan API 的核心流程是：

```text
GetSkillListForAI
        ↓
GetBuiltinSkillDetail / GetSkillAction
        ↓
ExecuteSkill / ExecuteSkillForCoze
        ↓
可信本地运行时
        ↓
结构化执行结果
```

推荐原则：

1. 先查询能力目录；
2. 优先复用匹配的数据库工作流；
3. 没有匹配工作流时，再查询内置技能详情；
4. 缺少必要参数时询问用户，不猜测路径、邮箱或凭据；
5. 副作用操作成功后立即停止；
6. 同一失败调用最多修正参数后重试一次。

## 2. 基础信息

### Base URL

本地运行示例：

```text
http://localhost:54124
```

如果通过 Visual Studio 或其他启动配置运行，端口可能不同。请以启动日志和 Swagger 页面为准。

### Content-Type

POST 请求使用：

```http
Content-Type: application/json
```

### 认证

生产环境应通过 API Key、网关、VPN、IP 白名单或其他访问控制保护本地运行时。具体认证 Header 和启用方式以当前部署配置为准，不应假设开发环境默认具备完整生产认证。

### Swagger

```text
http://localhost:54124/swagger
```

## 3. 核心接口总览

| API | 方法 | 路径 | 用途 |
|---|---|---|---|
| `GetSkillListForAI` | POST | `/api/Skills/GetSkillListForAI` | 获取工作流和内置技能摘要。 |
| `GetBuiltinSkillDetail` | POST | `/api/Skills/GetBuiltinSkillDetail` | 获取一个内置技能的详细定义。 |
| `GetBuiltinSkillManifest` | POST | `/api/Skills/GetBuiltinSkillManifest` | 获取完整技能清单，主要用于调试或文档生成。 |
| `GetSkillAction` | POST | `/api/Skills/GetSkillAction` | 获取数据库工作流的步骤定义。 |
| `ExecuteSkill` | POST | `/api/Skills/ExecuteSkill` | 统一执行内置技能、数据库工作流或临时工作流。 |
| `ExecuteSkillForCoze` | POST | `/api/Skills/ExecuteSkillForCoze` | 通过字符串参数兼容不便传递复杂 JSON 的 Agent 平台。 |

## 4. 获取能力目录

### `GetSkillListForAI`

```http
POST /api/Skills/GetSkillListForAI
```

请求体可以为空对象：

```json
{}
```

示例响应：

```json
{
  "success": true,
  "data": {
    "workflows": [
      {
        "skillCode": "capture_and_send_email",
        "AIDesc": "截图并发送邮件",
        "sourceType": "workflow",
        "needDetail": true
      }
    ],
    "builtins": [
      {
        "skillCode": "email_task",
        "AIDesc": "邮箱操作，支持搜索、读取、发送和附件下载",
        "sourceType": "builtin",
        "needDetail": true
      }
    ]
  }
}
```

字段说明：

| 字段 | 说明 |
|---|---|
| `workflows` | 数据库中保存的可复用工作流。 |
| `builtins` | 技能清单中登记的内置技能。 |
| `skillCode` | 技能或工作流标识。 |
| `AIDesc` | 面向 Agent 的能力摘要。 |
| `sourceType` | `workflow` 或 `builtin`。 |
| `needDetail` | 是否建议继续查询详细定义。 |

## 5. 获取内置技能详情

### `GetBuiltinSkillDetail`

```http
POST /api/Skills/GetBuiltinSkillDetail
```

请求示例：

```json
{
  "skillCode": "email_task"
}
```

该接口用于获取技能支持的动作、参数、约束和示例。Agent 应根据接口真实返回生成参数，不应长期依赖硬编码的技能字典。

## 6. 获取完整技能清单

### `GetBuiltinSkillManifest`

```http
POST /api/Skills/GetBuiltinSkillManifest
```

请求体：

```json
{}
```

该接口适合调试、文档生成或一次性读取完整定义。对于普通 Agent 调用，更推荐先获取摘要，再按需查询单个技能详情，以减少上下文占用。

## 7. 获取数据库工作流

### `GetSkillAction`

```http
POST /api/Skills/GetSkillAction
```

请求示例：

```json
{
  "skillCode": "capture_and_send_email"
}
```

示例响应：

```json
{
  "success": true,
  "data": {
    "skillCode": "capture_and_send_email",
    "remark": "截图并发送邮件",
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
          "subject": "屏幕截图",
          "body": "以下是截图",
          "insertImagePaths": [
            "{{step0.data.path}}"
          ]
        }
      }
    ]
  }
}
```

Agent 在执行工作流前应先读取其步骤和参数要求，不要只凭名称直接执行。

## 8. 统一执行入口

### `ExecuteSkill`

```http
POST /api/Skills/ExecuteSkill
```

顶层字段：

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `SkillCode` | string | 是 | 技能或工作流标识。 |
| `Arguments` | object | 否 | 单步技能参数或工作流输入参数。无参数时传 `{}`。 |
| `Steps` | array | 否 | 临时工作流的步骤。 |

### 执行内置技能

```json
{
  "SkillCode": "file_task",
  "Arguments": {
    "action": "search",
    "keyword": "报告",
    "ext": "docx"
  }
}
```

### 执行数据库工作流

```json
{
  "SkillCode": "capture_and_send_email",
  "Arguments": {}
}
```

### 执行临时工作流

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
        "subject": "屏幕截图",
        "body": "以下是自动截图",
        "insertImagePaths": [
          "{{step0.data.path}}"
        ]
      }
    }
  ]
}
```

执行模式由请求内容和数据库中是否存在同名工作流共同决定：

| 模式 | 说明 |
|---|---|
| `builtin` | 执行内置技能。 |
| `workflow` | 执行数据库工作流。 |
| `temp_workflow` | 执行请求中提交的临时工作流。 |

## 9. Coze 兼容执行入口

### `ExecuteSkillForCoze`

```http
POST /api/Skills/ExecuteSkillForCoze
```

请求体只有一个字符串字段：

```json
{
  "Json": "{\"skillCode\":\"email_task\",\"arguments\":{\"action\":\"search\",\"subjectKeyword\":\"通知\",\"maxCount\":10}}"
}
```

反序列化后的内容为：

```json
{
  "skillCode": "email_task",
  "arguments": {
    "action": "search",
    "subjectKeyword": "通知",
    "maxCount": 10
  }
}
```

该接口适用于只能传递字符串参数或不方便表达复杂嵌套对象的平台。

## 10. 工作流步骤与上下文

每个步骤使用：

```json
{
  "Action": "email_task",
  "Args": {
    "action": "send",
    "to": "someone@example.com",
    "subject": "测试",
    "body": "Hello"
  }
}
```

执行结果依次保存为：

```text
step0
step1
step2
...
```

后续步骤可以引用：

```text
{{step0.data.path}}
{{step0.data.firstPath}}
{{step1.result}}
```

## 11. 响应与错误处理

成功响应通常包含：

```json
{
  "success": true,
  "message": "执行成功",
  "data": {}
}
```

失败响应通常包含：

```json
{
  "success": false,
  "message": "参数错误",
  "errorCode": "INVALID_ARGUMENTS",
  "data": null
}
```

常见错误类型包括：

| 错误码 | 说明 |
|---|---|
| `SKILL_NOT_FOUND` | 技能不存在。 |
| `INVALID_ARGUMENTS` | 参数不合法。 |
| `MISSING_ARGUMENTS` | 缺少必要参数。 |
| `EMAIL_CONFIG_MISSING` | 邮箱配置缺失。 |
| `FILE_NOT_FOUND` | 文件不存在。 |
| `EXECUTION_FAILED` | 技能执行失败。 |
| `WORKFLOW_EXECUTION_FAILED` | 工作流执行失败。 |
| `SIDE_EFFECT_BLOCKED` | 副作用操作被阻止。 |
| `PERMISSION_DENIED` | 权限不足。 |
| `FORBIDDEN` | 安全策略拒绝。 |
| `TIMEOUT` | 执行超时。 |
| `INTERNAL_ERROR` | 服务器内部错误。 |

具体 HTTP 状态码和返回字段应以当前运行版本的 Swagger 与实际接口响应为准。

## 12. 管理与浏览器接口

项目还提供工作流管理接口和独立浏览器 Session 接口，例如：

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

这些接口主要面向管理端、调试工具或专用客户端。使用前应通过 Swagger 核对当前版本的请求结构。

## 13. 相关文档

- [内置技能参考](builtin-skills_zh-CN.md)
- [Agent 集成指南](agent-integration_zh-CN.md)
- [配置与安全控制](configuration-security_zh-CN.md)
- [故障排查](troubleshooting_zh-CN.md)
