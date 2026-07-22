# OpenTangYuan API 文档

> 本文档用于说明 OpenTangYuan 的核心 API。  
> 当前版本以 **Agent 调用链路** 和 **Workflow Runtime** 为重点，优先描述智能体实际需要使用的接口，而不是穷举所有管理接口。  
---

## 1. API 设计目标

OpenTangYuan 的 API 不是普通业务接口，而是面向智能体调用的 **Capability Discovery + Workflow Execution API**。

其设计目标包括：

1. 让外部 Agent 能够动态发现本地可用能力；
2. 支持 Workflow 优先执行，减少重复规划；
3. 支持 Builtin Skill 作为动态任务组合的兜底能力；
4. 通过统一执行入口支持单步技能、数据库 Workflow 和临时 Workflow；
5. 通过结构化响应降低大语言模型解析难度；
6. 通过本地 Runtime 保留真实执行能力与敏感数据边界。

典型调用链路如下：

```text
GetSkillListForAI
        ↓
GetBuiltinSkillDetail / GetSkillAction
        ↓
ExecuteSkill / ExecuteSkillForCoze
        ↓
Trusted Local Runtime
        ↓
Execution Result
```

---

## 2. 基础信息

### 2.1 Base URL

本地开发环境默认示例：

```text
http://localhost:54124
```

如果使用 Visual Studio / launchSettings.json 启动，端口可能不同，例如：

```text
http://localhost:5207
https://localhost:7026
```

请以实际启动日志或 Swagger 页面为准。

---

### 2.2 Content-Type

所有 POST 请求推荐使用：

```http
Content-Type: application/json
```

---

### 2.3 认证方式

OpenTangYuan 推荐使用 API-key 方式保护本地 Runtime。

示例：

```http
X-API-Key: your-api-key
```

> TODO：请根据当前项目实际认证 Header 名称更新本节。  
> 如果当前环境仅在内网或开发环境使用，也建议在生产环境中启用 API-key 或网关鉴权。

---

### 2.4 响应格式

OpenTangYuan 推荐统一返回结构化 JSON。

成功响应示例：

```json
{
  "success": true,
  "message": "执行成功",
  "data": {}
}
```

失败响应示例：

```json
{
  "success": false,
  "message": "参数错误",
  "errorCode": "INVALID_ARGUMENTS",
  "data": null
}
```

实际后端中可能使用 `ResponseHelper.Success(...)` 和 `ResponseHelper.Fail(...)` 统一封装返回值。

---

## 3. Agent 调用原则

外部 Agent 调用 OpenTangYuan 时，推荐遵循以下顺序：

1. **先发现能力**：调用 `GetSkillListForAI`；
2. **优先匹配 Workflow**：如果已有 Workflow 能覆盖任务，调用 `GetSkillAction` 获取步骤；
3. **无 Workflow 时查询 Builtin Skill**：调用 `GetBuiltinSkillDetail`；
4. **生成参数并执行**：调用 `ExecuteSkill` 或 `ExecuteSkillForCoze`；
5. **执行完成后停止**：对于发送邮件、复制文件、下载附件、移动文件、打印等副作用操作，成功后不要重复调用；
6. **缺少参数时询问用户**：不要猜测本地路径、邮箱、收件人或敏感参数；
7. **失败时最多重试一次**：同一技能失败后只允许修正参数后重试一次，避免无限循环。

---

## 4. 核心 API 总览

| API | 方法 | 路径 | 主要用途 |
|---|---|---|---|
| GetSkillListForAI | POST | `/api/Skills/GetSkillListForAI` | 获取 Workflow 与 Builtin Skill 摘要 |
| GetBuiltinSkillDetail | POST | `/api/Skills/GetBuiltinSkillDetail` | 获取某个 Builtin Skill 的详细定义 |
| GetBuiltinSkillManifest | POST | `/api/Skills/GetBuiltinSkillManifest` | 获取完整 Builtin Skill Manifest |
| GetSkillAction | POST | `/api/Skills/GetSkillAction` | 获取某个 Workflow 的步骤定义 |
| ExecuteSkill | POST | `/api/Skills/ExecuteSkill` | 统一执行入口 |
| ExecuteSkillForCoze | POST | `/api/Skills/ExecuteSkillForCoze` | Coze 兼容执行入口 |
| Browser Run | POST | `/AiApi/Browser/run` | 执行浏览器动作序列 |
| Browser Start | POST | `/AiApi/Browser/start` | 创建浏览器 Session |
| Browser Close | POST | `/AiApi/Browser/close` | 关闭浏览器 Session |
| Browser Sessions | GET | `/AiApi/Browser/sessions` | 查看浏览器 Session |
| SaveSkillAction | POST | `/api/Skills/SaveSkillAction` | 保存 Workflow |
| GetSkillList | POST | `/api/Skills/GetSkillList` | 获取全部 Workflow |
| DeleteSkill | POST | `/api/Skills/DeleteSkill` | 删除 Workflow |
| GetAllSkillCodes | POST | `/api/Skills/GetAllSkillCodes` | 获取全部 SkillCode |

---

# 5. Capability Discovery APIs

## 5.1 获取技能目录：GetSkillListForAI

```http
POST /api/Skills/GetSkillListForAI
```

### 用途

给外部 Agent 返回当前系统可用能力摘要，包括：

- 数据库中定义的 Workflow；
- `skill-manifest.json` 中定义的 Builtin Skill。

Agent 应优先查看 `workflows` 是否已有可复用流程。如果没有匹配 Workflow，再查看 `builtins` 并查询具体技能详情。

---

### 请求体

无需请求体。

```json
{}
```

---

### 响应示例

```json
{
  "success": true,
  "data": {
    "workflows": [
      {
        "skillCode": "office_check_unread_messages",
        "AIDesc": "检查办公系统未读消息并推送",
        "sourceType": "workflow",
        "needDetail": true
      }
    ],
    "builtins": [
      {
        "skillCode": "email_task",
        "AIDesc": "邮箱操作，支持搜索、读取、发送、附件下载等",
        "sourceType": "builtin",
        "needDetail": true
      },
      {
        "skillCode": "file_task",
        "AIDesc": "文件操作，支持搜索、复制、移动、重命名和创建目录",
        "sourceType": "builtin",
        "needDetail": true
      }
    ]
  }
}
```

---

### 字段说明

| 字段 | 类型 | 说明 |
|---|---|---|
| `workflows` | array | 数据库中保存的可复用 Workflow |
| `builtins` | array | Manifest 中定义的 Builtin Skill |
| `skillCode` | string | 技能或 Workflow 标识 |
| `AIDesc` | string | 给智能体阅读的能力描述 |
| `sourceType` | string | `workflow` 或 `builtin` |
| `needDetail` | bool | 是否需要继续查询详情 |

---

## 5.2 获取 Builtin Skill 详情：GetBuiltinSkillDetail

```http
POST /api/Skills/GetBuiltinSkillDetail
```

### 用途

根据 `skillCode` 获取某个 Builtin Skill 的详细定义，包括参数、动作和示例。

适用于：

- Agent 需要生成技能参数；
- Agent 需要了解某个技能支持哪些 action；
- Agent 需要根据 manifest 进行动态任务组合。

---

### 请求示例

```json
{
  "skillCode": "email_task"
}
```

---

### 响应示例

```json
{
  "success": true,
  "data": {
    "skillCode": "email_task",
    "AIDesc": "邮箱操作。支持 send, search, read, download_attachments, reply, mark_read, save_eml。",
    "actions": [
      "send",
      "search",
      "read",
      "download_attachments",
      "reply",
      "mark_read",
      "save_eml"
    ],
    "args": {
      "action": "send | search | read | download_attachments | reply | mark_read | save_eml",
      "to": "收件人邮箱",
      "subject": "邮件主题",
      "body": "邮件正文",
      "attachments": "附件路径数组",
      "insertImagePaths": "插入正文的图片路径数组"
    }
  }
}
```

> TODO：请根据真实 `skill-manifest.json` 更新响应示例。

---

### 常见错误

| 状态 | 说明 |
|---|---|
| 400 | `SkillCode` 为空 |
| 404 | 未找到 manifest 或未找到该 Builtin Skill |
| 500 | manifest JSON 格式错误或读取失败 |

---

## 5.3 获取完整 Builtin Manifest：GetBuiltinSkillManifest

```http
POST /api/Skills/GetBuiltinSkillManifest
```

### 用途

返回完整的 `skill-manifest.json` 内容。

该接口适合：

- 调试；
- 文档生成；
- 外部系统一次性读取所有 Builtin Skill 定义。

对大语言模型 Agent 而言，通常不建议每次都调用完整 manifest，因为可能占用较多上下文。更推荐先调用 `GetSkillListForAI`，再按需调用 `GetBuiltinSkillDetail`。

---

### 请求体

```json
{}
```

---

### 响应示例

```json
{
  "success": true,
  "data": {
    "builtins": [
      {
        "skillCode": "email_task",
        "AIDesc": "邮箱操作"
      },
      {
        "skillCode": "browser_task",
        "AIDesc": "浏览器自动化"
      }
    ]
  }
}
```

---

## 5.4 获取 Workflow 步骤：GetSkillAction

```http
POST /api/Skills/GetSkillAction
```

### 用途

根据 `SkillCode` 获取数据库中保存的 Workflow 定义。

适用于：

- Agent 在 `GetSkillListForAI` 中发现可用 Workflow 后，查询该 Workflow 的具体步骤；
- 管理端查看 Workflow 定义；
- 调试 Workflow 步骤。

---

### 请求示例

```json
{
  "skillCode": "capture_and_send_email"
}
```

---

### 响应示例

```json
{
  "success": true,
  "data": {
    "skillCode": "capture_and_send_email",
    "remark": "截图并发送邮件",
    "skillType": "workflow",
    "updateTime": "2026-01-01 10:00:00",
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

---

### 常见错误

| 状态 | 说明 |
|---|---|
| 400 | `SkillCode` 为空 |
| 404 | 未找到该 Workflow |
| 500 | Workflow JSON 格式错误 |

---

# 6. Execution APIs

## 6.1 统一执行入口：ExecuteSkill

```http
POST /api/Skills/ExecuteSkill
```

### 用途

OpenTangYuan 的统一执行入口。

它支持三类执行模式：

| 模式 | 判断方式 | 说明 |
|---|---|---|
| temporary workflow | 请求中包含 `Steps` 且不为空 | 直接执行临时 Workflow |
| database workflow | 数据库中存在同名 `SkillCode` | 执行预定义 Workflow |
| builtin skill | 无 Steps 且数据库中无同名 Workflow | 执行 Builtin Skill |

---

### 请求字段

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `SkillCode` | string | 是 | 技能或 Workflow 标识 |
| `Arguments` | object | 否 | 单步技能参数或 Workflow 输入参数 |
| `Steps` | array | 否 | 临时 Workflow 步骤 |

---

### 示例一：执行 Builtin Skill

```json
{
  "SkillCode": "email_task",
  "Arguments": {
    "action": "search",
    "subjectKeyword": "通知",
    "maxCount": 10,
    "contextKey": "mail_default"
  }
}
```

---

### 示例二：执行数据库 Workflow

```json
{
  "SkillCode": "office_check_unread_messages",
  "Arguments": {}
}
```

---

### 示例三：执行临时 Workflow

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
        "body": "以下是截图",
        "insertImagePaths": [
          "{{step0.data.path}}"
        ]
      }
    }
  ]
}
```

---

### 响应示例

```json
{
  "success": true,
  "data": {
    "skillCode": "temp_task",
    "executeMode": "temp_workflow",
    "result": {
      "success": true,
      "msg": "workflow 执行完成",
      "totalSteps": 2,
      "completedSteps": 2,
      "lastResult": {
        "success": true,
        "skillCode": "email_task",
        "type": "send_email",
        "text": "邮件发送完成"
      }
    }
  }
}
```

---

### 执行模式说明

| `executeMode` | 说明 |
|---|---|
| `temp_workflow` | 临时 Workflow |
| `workflow` | 数据库中保存的 Workflow |
| `builtin` | Builtin Skill |

---

### 常见错误

| 状态 | 说明 |
|---|---|
| 400 | 参数错误或不支持的技能 |
| 403 | 路径、工具或操作不被允许 |
| 404 | 目标文件不存在 |
| 500 | 服务器内部错误 |

---

## 6.2 Coze 兼容执行入口：ExecuteSkillForCoze

```http
POST /api/Skills/ExecuteSkillForCoze
```

### 用途

Coze 等平台有时对复杂嵌套对象参数支持有限，因此 OpenTangYuan 提供 `ExecuteSkillForCoze`。

该接口将完整执行参数序列化为字符串，放入 `Json` 字段中。

---

### 请求字段

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `Json` | string | 是 | 序列化后的执行参数 |

---

### 请求示例

```json
{
  "Json": "{\"skillCode\":\"email_task\",\"arguments\":{\"action\":\"search\",\"subjectKeyword\":\"通知\",\"maxCount\":10}}"
}
```

---

### 反序列化后的结构

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

---

### 响应示例

```json
{
  "success": true,
  "message": "执行成功",
  "skillCode": "email_task",
  "executeMode": "builtin",
  "resultType": "search_email",
  "resultText": "找到 3 封邮件",
  "resultList": [
    "1. 关于会议通知 | sender@example.com | 2026-01-01 | 有附件"
  ],
  "resultValue": "mailref-xxx",
  "resultData": {}
}
```

---

### 适用场景

- Coze 插件参数只能传字符串；
- Agent 平台不方便传递复杂 JSON；
- 需要统一处理 Coze 返回格式；
- 需要自动扁平化 Workflow 或 Builtin Skill 的执行结果。

---

# 7. Workflow Runtime

## 7.1 Step 结构

Workflow 中每一步使用以下结构：

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

字段说明：

| 字段 | 类型 | 说明 |
|---|---|---|
| `Action` | string | 要执行的 Builtin Skill |
| `Args` | object | 该步骤的参数 |

---

## 7.2 上下文变量

Workflow Runtime 会将每一步结果写入上下文：

```text
step0
step1
step2
...
```

后续步骤可通过模板变量引用前序结果：

```text
{{step0}}
{{step0.path}}
{{step0.data.path}}
{{step0.data.firstPath}}
{{step1.result}}
```

---

## 7.3 示例：文件搜索、打开、截图并发送邮件

```json
{
  "SkillCode": "temp_task",
  "Arguments": {},
  "Steps": [
    {
      "Action": "file_task",
      "Args": {
        "action": "search",
        "keyword": "2026创AI案例征集指南",
        "ext": "docx"
      }
    },
    {
      "Action": "open_task",
      "Args": {
        "path": "{{step0.data.firstPath}}"
      }
    },
    {
      "Action": "screenshot_task",
      "Args": {}
    },
    {
      "Action": "email_task",
      "Args": {
        "action": "send",
        "to": "someone@example.com",
        "subject": "文件截图与附件",
        "body": "以下是自动截图，文件已作为附件发送。",
        "insertImagePaths": [
          "{{step2.data.path}}"
        ],
        "attachments": [
          "{{step0.data.firstPath}}"
        ]
      }
    }
  ]
}
```

> TODO：请根据实际 `screenshot_task` 返回字段确认 `{{step2.data.path}}` 是否准确。

---

## 7.4 Debug 模式

Workflow Runtime 支持通过 `Arguments.debug = true` 返回详细执行日志。

示例：

```json
{
  "SkillCode": "temp_task",
  "Arguments": {
    "debug": true
  },
  "Steps": [
    {
      "Action": "screenshot_task",
      "Args": {}
    }
  ]
}
```

Debug 模式下可返回每一步：

- stepIndex；
- action；
- resolved args；
- success；
- result summary；
- error message。

---

# 8. Builtin Skill APIs

Builtin Skill 统一通过 `ExecuteSkill` 执行。

---

## 8.1 email_task

### 支持动作

| action | 说明 |
|---|---|
| `send` | 发送邮件 |
| `search` | 搜索邮件 |
| `read` | 读取邮件正文 |
| `download_attachments` | 下载附件 |
| `reply` | 回复邮件 |
| `mark_read` | 标记已读 |
| `save_eml` | 保存邮件为 eml 文件 |

---

### 搜索邮件

```json
{
  "SkillCode": "email_task",
  "Arguments": {
    "action": "search",
    "subjectKeyword": "通知",
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

### 读取邮件

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

### 下载附件

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

### 发送邮件

```json
{
  "SkillCode": "email_task",
  "Arguments": {
    "action": "send",
    "to": "someone@example.com",
    "subject": "测试邮件",
    "body": "这是一封自动发送的邮件。",
    "attachments": [
      "D:\\Files\\report.docx"
    ],
    "insertImagePaths": [
      "D:\\Images\\screen.png"
    ]
  }
}
```

---

## 8.2 file_task

### 支持动作

| action | 说明 |
|---|---|
| `search` | 搜索文件 |
| `copy` | 复制文件 |
| `move` | 移动文件 |
| `copy_many` | 批量复制 |
| `move_many` | 批量移动 |
| `rename` | 重命名 |
| `mkdir` | 创建目录 |

---

### 搜索文件

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

### 复制文件

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

### 创建目录

```json
{
  "SkillCode": "file_task",
  "Arguments": {
    "action": "mkdir",
    "from": "D:\\Target"
  }
}
```

---

## 8.3 browser_task

### 用途

执行浏览器自动化任务。

支持：

- 打开网页；
- 等待元素；
- 点击；
- 输入；
- 提取文本；
- 获取列表；
- 截图；
- 下载；
- 保持 session；
- 关闭 session。

---

### ExecuteSkill 调用示例

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

---

## 8.4 wechat_task

### 支持动作

| action | 说明 |
|---|---|
| `text` | 发送文本消息 |
| `markdown` | 发送 Markdown 消息 |
| `card` | 发送图文卡片 |

---

### 发送文本消息

```json
{
  "SkillCode": "wechat_task",
  "Arguments": {
    "action": "text",
    "content": "任务已完成",
    "isAtAll": false
  }
}
```

---

## 8.5 open_task

```json
{
  "SkillCode": "open_task",
  "Arguments": {
    "path": "D:\\Files\\report.docx"
  }
}
```

---

## 8.6 print_task

```json
{
  "SkillCode": "print_task",
  "Arguments": {
    "path": "D:\\Files\\report.docx"
  }
}
```

---

## 8.7 screenshot_task

```json
{
  "SkillCode": "screenshot_task",
  "Arguments": {}
}
```

> TODO：如果 screenshot_task 需要 `action` 字段，请根据实际代码更新。

---

## 8.8 tool_task

```json
{
  "SkillCode": "tool_task",
  "Arguments": {
    "exePath": "D:\\Tools\\LmyTools.exe",
    "arguments": "--help",
    "timeout": 10
  }
}
```

注意：`tool_task` 只能调用白名单中的 exe。

---

# 9. Browser API

除 `browser_task` 外，OpenTangYuan 也提供独立浏览器 API。

---

## 9.1 创建浏览器 Session

```http
POST /AiApi/Browser/start
```

响应示例：

```json
{
  "success": true,
  "sessionId": "session-xxx",
  "message": "Session 创建成功"
}
```

---

## 9.2 执行浏览器动作

```http
POST /AiApi/Browser/run
```

请求示例：

```json
{
  "sessionId": "session-xxx",
  "actions": [
    {
      "type": "goto",
      "url": "https://example.com"
    },
    {
      "type": "get_text",
      "selector": "body"
    }
  ],
  "closeSession": false
}
```

响应示例：

```json
{
  "success": true,
  "sessionId": "session-xxx",
  "page": {
    "url": "https://example.com",
    "title": "Example"
  },
  "result": {
    "type": "text",
    "text": "Example Domain",
    "list": [],
    "data": {}
  },
  "outputs": [],
  "logs": []
}
```

---

## 9.3 关闭浏览器 Session

```http
POST /AiApi/Browser/close
```

请求示例：

```json
{
  "sessionId": "session-xxx"
}
```

---

## 9.4 查看浏览器 Sessions

```http
GET /AiApi/Browser/sessions
```

---

# 10. Workflow 管理 API

以下 API 主要面向管理端或开发者，不建议普通 Agent 高频调用。

---

## 10.1 保存 Workflow：SaveSkillAction

```http
POST /api/Skills/SaveSkillAction
```

请求示例：

```json
{
  "skillCode": "capture_and_send_email",
  "skillActions": "[{\"Action\":\"screenshot_task\",\"Args\":{}},{\"Action\":\"email_task\",\"Args\":{\"action\":\"send\",\"to\":\"someone@example.com\",\"subject\":\"截图\",\"body\":\"见正文\",\"insertImagePaths\":[\"{{step0.data.path}}\"]}}]",
  "remark": "截图并发送邮件",
  "skillType": "workflow",
  "updateTime": "2026-01-01 10:00:00"
}
```

> 注意：当前 `skillActions` 通常为 JSON 字符串，内部存储为步骤数组。

---

## 10.2 获取全部 Workflow：GetSkillList

```http
POST /api/Skills/GetSkillList
```

---

## 10.3 删除 Workflow：DeleteSkill

```http
POST /api/Skills/DeleteSkill
```

请求示例：

```json
{
  "skillCode": "capture_and_send_email"
}
```

---

## 10.4 获取全部 SkillCode：GetAllSkillCodes

```http
POST /api/Skills/GetAllSkillCodes
```

---

# 11. 安全注意事项

OpenTangYuan 能够执行具有副作用的本地操作，因此应当在受信任环境中运行。

建议：

1. 不要将 API 服务直接暴露到公网；
2. 生产环境必须启用 API-key 或网关鉴权；
3. 文件路径必须限制在 `AllowedRoots` 中；
4. 外部程序必须通过 `AllowedExeNames` 白名单控制；
5. 发送邮件、删除文件、移动文件、打印等操作应启用审计；
6. 对高风险操作增加用户确认或审批；
7. 不要将邮箱授权码、Webhook Key、API Token 提交到仓库；
8. 对所有关键执行动作记录日志；
9. 对 LLM 生成的参数进行校验，不要直接信任模型输出；
10. 对同一失败调用限制重试次数，避免循环执行。

---

# 12. 常见错误码

| 错误码 | 说明 |
|---|---|
| `SKILL_NOT_FOUND` | 技能不存在 |
| `INVALID_ARGUMENTS` | 参数不合法 |
| `MISSING_ARGUMENTS` | 缺少必要参数 |
| `EMAIL_CONFIG_MISSING` | 邮箱配置缺失 |
| `FILE_NOT_FOUND` | 文件不存在 |
| `EXECUTION_FAILED` | 技能执行失败 |
| `WORKFLOW_EXECUTION_FAILED` | Workflow 执行失败 |
| `SIDE_EFFECT_BLOCKED` | 副作用操作被阻止重复执行 |
| `PERMISSION_DENIED` | 权限不足 |
| `FORBIDDEN` | 安全策略拒绝 |
| `TIMEOUT` | 执行超时 |
| `INTERNAL_ERROR` | 服务器内部错误 |

---

# 13. Agent Prompt 建议

建议在外部 Agent 中写入以下调用规则：

```text
你是 OpenTangYuan 的任务编排智能体。

调用原则：
1. 先调用 GetSkillListForAI 查询可用能力。
2. 如果存在匹配 Workflow，优先使用 Workflow。
3. 如果没有匹配 Workflow，再查询 Builtin Skill 详情。
4. 执行任务时调用 ExecuteSkill 或 ExecuteSkillForCoze。
5. 多步骤任务使用 Steps。
6. 后续步骤引用前一步结果时使用 {{step0.data.xxx}}。
7. 对发送、复制、移动、删除、下载附件、标记已读、保存文件、打印等副作用动作，成功后立即停止，不要重复执行。
8. 缺少路径、邮箱、收件人等必要参数时先询问用户。
9. 同一技能失败时，只允许修正参数后最多重试一次。
10. 不要猜测本地路径，不要编造文件名，不要绕过白名单。
```

---

# 14. 后续待完善

建议后续继续补充：

- Swagger 截图；
- OpenAPI JSON；
- `skill-manifest.json` 完整说明；
- `docs/workflow.md`；
- `docs/builtins.md`；
- `docs/deployment.md`；
- `docs/security.md`；
- smoke test 脚本；
- API 自动化测试；
- 示例配置 `appsettings.example.json`；
- Release 与 Zenodo DOI。

---

## 许可证

本项目采用 MIT License。

详见仓库根目录 `LICENSE` 文件。
