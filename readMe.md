# OpenTangYuan

<p align="center">
  <img src="readme/logo.png" alt="OpenTangYuan Logo" width="160">
</p>

<h1 align="center">OpenTangYuan</h1>

<p align="center">
  面向办公自动化场景的 Agent Workflow Runtime
</p>

<p align="center">
  通过云端智能规划、本地可信执行和 Workflow 编排，连接邮件、文件、浏览器、企业微信、本地工具与企业系统。
</p>

<p align="center">
  <a href="#快速开始">快速开始</a> ·
  <a href="#核心特性">核心特性</a> ·
  <a href="#系统架构">系统架构</a> ·
  <a href="#内置技能">内置技能</a> ·
  <a href="#coze-智能体接入">Coze 接入</a> ·
  <a href="#文档导航">文档</a>
</p>

<p align="center">
  <a href="#"><img src="https://img.shields.io/badge/.NET-8.0-purple" alt=".NET 8"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green" alt="License MIT"></a>
  <a href="#"><img src="https://img.shields.io/badge/CI-GitHub%20Actions-blue" alt="CI"></a>
  <a href="#"><img src="https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-blue" alt="Platform"></a>
  <a href="https://gitee.com/yourname/OpenTangYuan"><img src="https://img.shields.io/badge/Gitee-OpenTangYuan-C71D23?logo=gitee&logoColor=white" alt="Gitee"></a>
</p>

---

## 项目简介

**OpenTangYuan** 是一个面向办公自动化场景的 **Agent Workflow Runtime**。

它不仅提供普通的工具调用能力，还提供 Workflow 编排、上下文管理、技能路由、副作用控制、多步骤执行、调试重试和本地可信执行能力。

OpenTangYuan 可以让 AI 智能体通过自然语言任务自动完成：

- 邮件发送、搜索、读取、回复和附件处理
- 网页打开、截图、内容提取和自动化操作
- 本地文件搜索、复制、移动、重命名和打开
- 本地屏幕或窗口截图
- 企业微信通知发送
- 本地工具或可执行程序调用
- 自定义 Workflow 编排

项目定位：

> OpenTangYuan is an Agent Workflow Runtime for Office Automation.  
> It enables cloud-based AI planning while keeping data and execution inside trusted local environments through workflow orchestration, skill routing, and secure runtime management.

中文定位：

> OpenTangYuan 是一个面向办公自动化场景的 Agent Workflow Runtime，通过云端智能规划、本地可信执行和 Workflow 编排，实现企业级智能体落地能力。

---

## 项目预览

> 调用即有技能

![Demo 1](readme/images/demo-1.png)

> 自生成组合复杂任务

![Demo 2](readme/images/demo-2.png)


## 目录

- [项目简介](#项目简介)
- [项目预览](#项目预览)
- [核心特性](#核心特性)
- [系统架构](#系统架构)
- [工作流程](#工作流程)
- [技术栈](#技术栈)
- [快速开始](#快速开始)
- [Docker 部署](#docker-部署)
- [配置说明](#配置说明)
- [内置技能](#内置技能)
- [Workflow 自定义](#workflow-自定义)
- [API 文档](#api-文档)
- [使用示例](#使用示例)
- [Coze 智能体接入](#coze-智能体接入)
- [响应格式](#响应格式)
- [安全说明](#安全说明)
- [文档导航](#文档导航)
- [开发与贡献](#开发与贡献)
- [路线图](#路线图)
- [常见问题](#常见问题)
- [社区与支持](#社区与支持)
- [许可证](#许可证)

---

## 核心特性

### Agent Workflow Runtime

OpenTangYuan 不只是一个 Tool Calling 框架，而是一个面向智能体任务落地的 Workflow Runtime。

它支持：

- 技能发现
- 技能详情查询
- 参数生成
- 多步骤任务编排
- 上下文引用
- 本地可信执行
- 执行结果回传

### Workflow 与 Builtin 技能组合

支持将内置原子技能与自定义 Workflow 组合使用，满足复杂办公自动化任务需求。

例如：

```text
用户请求：帮我打开网页，截图后发给同事
系统执行：browser_task → screenshot_task → email_task
```

### 多步骤任务编排

通过 `temp_task` 支持临时多步骤任务执行。

典型场景：

- 截图后发送邮件
- 打开网页后提取正文
- 搜索文件后打开
- 下载邮件附件后保存到指定目录
- 调用本地工具后发送执行结果

### 上下文管理

多步骤任务中，后续步骤可以引用前一步结果。

示例：

```text
{{step0.data.path}}
```

这使得以下流程成为可能：

```text
步骤 1：截图并返回图片路径
步骤 2：将步骤 1 的图片路径插入邮件正文
```

### 技能路由

智能体无需提前记住所有技能参数，而是通过以下流程动态获取能力：

```text
GetSkillListForAI
  ↓
GetSkillAction / GetBuiltinSkillDetail
  ↓
ExecuteSkill / ExecuteSkillForCoze
```

### 副作用操作控制

对具有副作用的动作进行重复执行限制，避免误操作。

副作用动作包括但不限于：

- 发送邮件
- 回复邮件
- 复制文件
- 移动文件
- 删除文件
- 下载附件
- 标记已读
- 保存文件
- 打印文件

### 调试与重试机制

支持查看智能体内部决策、技能选择、参数生成、步骤执行结果和错误信息。

同一技能失败时，建议只允许修正参数后最多重试一次，避免无限循环调用。

---

## 系统架构


![Architecture](readme/images/architecture.png)

```text
云端 AI 规划 + 可信本地技能执行
OpenTangYuan Agent Workflow Runtime Architecture
Cloud-based AI Planning & Trusted Local Skill Execution
```

OpenTangYuan 建议按照以下层次理解：

| 层级 | 说明 |
|---|---|
| User Interaction Layer | 用户入口，包括 Web、移动端、聊天入口、API、SDK、Coze、Dify、GPTs 等 |
| OpenTangYuan Orchestration Layer | 负责意图理解、Workflow 规划、上下文管理、技能路由 |
| Secure Execution Channel | 负责云端规划层与本地执行层之间的安全通信 |
| Trusted Local Runtime Layer | 负责本地鉴权、会话管理、Workflow 执行、Skill 调用与结果封装 |
| Enterprise Integration Layer | 对接邮件、OA、文件系统、企业微信、浏览器、本地工具和自定义系统 |
| Governance, Security & Compliance | 负责隐私保护、访问控制、执行审计、监控告警等 |

---

## 工作流程


典型执行流程：

1. 用户通过 Coze、Web、API 或其他智能体入口提交任务。
2. 智能体调用 `GetSkillListForAI` 获取可用技能。
3. 如技能需要详情，则调用：
   - Workflow：`GetSkillAction`
   - Builtin：`GetBuiltinSkillDetail`
4. 智能体根据技能详情生成参数。
5. 调用 `ExecuteSkill` 或 `ExecuteSkillForCoze` 执行任务。
6. 本地 Runtime 调用对应 Builtin Skill 或 Workflow。
7. 每一步执行结果写入上下文。
8. 最终返回执行结果。
9. 如果任务已完成，智能体立即停止，避免重复执行。

---

## 技术栈

| 技术 | 用途 |
|---|---|
| .NET 8 | 后端运行框架 |
| C# 10+ | 主要开发语言 |
| SQLite | Workflow 数据存储 |
| MailKit | SMTP / IMAP 邮件处理 |
| Playwright | 浏览器自动化 |
| Windows 原生命令 | 本地文件打开、打印等系统操作 |
| 企业微信 Webhook / API | 企业微信消息通知 |
| REST API | 技能查询与任务执行接口 |

---

## 快速开始

### 环境要求

- Windows / Linux / macOS
- .NET 8 SDK
- SQLite
- 可选：邮箱账号、企业微信机器人、本地浏览器环境等

### 克隆项目

>gitee仓库
```bash
https://gitee.com/l00f/open-tang-yuan.git
```
>github仓库
```bash
https://github.com/wrnas/OpenTangYuan.git
```

### 安装依赖

```bash
dotnet restore
```

### 启动服务

```bash
dotnet run --urls "http://localhost:54124"
```

### 验证服务

```bash
curl -X POST http://localhost:54124/api/Skills/GetSkillListForAI
```

如果服务正常，将返回内置技能和 Workflow 列表。

---

## Docker 部署

> 如当前项目暂未提供 Docker 支持，可先保留本节，后续补充 Dockerfile 和 docker-compose.yml。

### 构建镜像

```bash
docker build -t opentangyuan .
```

### 启动容器

```bash
docker run -d \
  --name opentangyuan \
  -p 54124:54124 \
  opentangyuan
```

### Docker Compose 示例

```yaml
version: "3.9"

services:
  opentangyuan:
    image: opentangyuan:latest
    container_name: opentangyuan
    ports:
      - "54124:54124"
    volumes:
      - ./data:/app/data
      - ./appsettings.json:/app/appsettings.json
    restart: unless-stopped
```

---

## 配置说明

编辑 `appsettings.json`：

```json
{
  "EmailSettings": {
    "SmtpServer": "smtp.163.com",
    "SmtpPort": 465,
    "SmtpUseSsl": true,
    "SenderEmail": "your-email@example.com",
    "SenderPassword": "your-authorization-code",
    "ImapServer": "imap.163.com",
    "ImapPort": 993,
    "ImapUseSsl": true
  }
}
```

### 配置项说明

| 配置项 | 必填 | 默认值 | 说明 |
|---|---|---|---|
| `EmailSettings:SmtpServer` | 否 | - | SMTP 服务器地址 |
| `EmailSettings:SmtpPort` | 否 | `465` | SMTP 端口 |
| `EmailSettings:SmtpUseSsl` | 否 | `true` | 是否启用 SMTP SSL |
| `EmailSettings:SenderEmail` | 否 | - | 发件人邮箱 |
| `EmailSettings:SenderPassword` | 否 | - | 邮箱客户端授权码 |
| `EmailSettings:ImapServer` | 否 | - | IMAP 服务器地址 |
| `EmailSettings:ImapPort` | 否 | `993` | IMAP 端口 |
| `EmailSettings:ImapUseSsl` | 否 | `true` | 是否启用 IMAP SSL |
| `Database:ConnectionString` | 是 | - | SQLite 数据库连接字符串 |
| `DebugMode` | 否 | `false` | 是否开启调试模式 |
| `AllowedHosts` | 否 | `*` | ASP.NET Core 允许访问的主机 |

> 请根据实际项目配置补充或调整以上字段。

### 敏感信息建议

请勿将以下内容提交到代码仓库：

- 邮箱登录密码
- 邮箱客户端授权码
- 企业微信 Webhook Key
- API Token
- 内网系统账号密码
- 数据库连接密钥

建议使用：

- 环境变量
- 用户机密配置
- Docker Secret
- CI/CD Secret
- 独立的生产配置文件

---

## 内置技能

| 技能名称 | 功能描述 |
|---|---|
| `email_task` | 发送邮件、搜索邮件、读取正文、下载附件、回复邮件、标记已读、保存 eml |
| `wechat_task` | 发送企业微信 text、markdown、card 消息 |
| `browser_task` | 打开网页、网页截图、提取网页内容 |
| `file_task` | 文件搜索、复制、移动、重命名、创建目录 |
| `open_task` | 打开本地文件 |
| `print_task` | 打印本地文件 |
| `tool_task` | 调用本地工具或可执行程序 |
| `screenshot_task` | 截取本地屏幕或指定窗口 |

Workflow 可通过 SQLite 配置，并支持组合已有 Builtin 技能。

---

## Workflow 自定义

OpenTangYuan 支持通过 Workflow 组合已有 Builtin 技能，实现更复杂的业务自动化流程。

### 示例：截图并发送邮件

```json
{
  "name": "截图并发送邮件",
  "code": "capture_and_send_email",
  "description": "截取当前屏幕并通过邮件发送给指定收件人",
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
        "body": "以下是自动截取的屏幕截图",
        "insertImagePaths": [
          "{{step0.data.path}}"
        ]
      }
    }
  ]
}
```

> 请根据实际 Workflow 存储结构调整以上示例。

### Workflow 设计建议

设计 Workflow 时建议遵循以下原则：

1. 一个 Workflow 只解决一个明确场景。
2. 每一步只调用一个明确 Skill。
3. 对发送、删除、移动、打印等副作用操作增加确认或审计。
4. 多步引用统一使用 `{{stepN.data.xxx}}` 语法。
5. 不要在 Workflow 中硬编码用户隐私数据或敏感路径。

---

## API 文档

### 获取技能目录

```http
POST /api/Skills/GetSkillListForAI
```

返回示例：

```json
{
  "workflows": [],
  "builtins": []
}
```

该接口用于给 AI 智能体查看当前可用能力。

### 获取技能详情

| 类型 | 接口 |
|---|---|
| Workflow | `GetSkillAction` |
| Builtin | `GetBuiltinSkillDetail` |

当 `GetSkillListForAI` 返回 `needDetail=true` 时，智能体应按类型继续查询详情。

### 执行技能

```http
POST /api/Skills/ExecuteSkill
```

请求体字段：

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `SkillCode` | string | 是 | 技能标识，例如 `temp_task`、`email_task` |
| `Arguments` | object | 否 | 单步任务参数 |
| `Steps` | array | 否 | 多步任务参数，每步包含 `Action` 和 `Args` |

### Coze 执行接口

```http
POST /api/Skills/ExecuteSkillForCoze
```

调用 `ExecuteSkillForCoze` 时，将以下结构序列化后放入 `Json` 字段：

```json
{
  "skillCode": "email_task",
  "arguments": {
    "action": "search",
    "query": "from:user@example.com",
    "limit": 10
  }
}
```

### Swagger 调试接口

```http
GET    /api/Skills/GetSkillList
POST   /api/Skills/SaveSkillAction
DELETE /api/Skills/DeleteSkill
```

说明：

| 接口 | 说明 |
|---|---|
| `GetSkillList` | 获取全部 Workflow |
| `SaveSkillAction` | 保存自定义 Workflow |
| `DeleteSkill` | 删除指定 Workflow |

---

## 使用示例

### 示例一：截图并发送邮件

```json
POST /api/Skills/ExecuteSkill
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

### 示例二：查询邮件列表

```json
POST /api/Skills/ExecuteSkill
{
  "SkillCode": "email_task",
  "Arguments": {
    "action": "search",
    "query": "from:user@example.com",
    "limit": 10
  }
}
```

### 示例三：打开本地文件

```json
POST /api/Skills/ExecuteSkill
{
  "SkillCode": "open_task",
  "Arguments": {
    "filePath": "C:\\Documents\\report.docx"
  }
}
```

### 示例四：打开网页并提取内容

```json
POST /api/Skills/ExecuteSkill
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
    ]
  }
}
```

### 示例五：多步任务后发送企业微信通知

```json
POST /api/Skills/ExecuteSkill
{
  "SkillCode": "temp_task",
  "Arguments": {},
  "Steps": [
    {
      "Action": "browser_task",
      "Args": {
        "actions": [
          {
            "type": "goto",
            "url": "https://oa.example.com"
          },
          {
            "type": "wait_for",
            "selector": "body"
          },
          {
            "type": "get_text",
            "selector": "body"
          }
        ]
      }
    },
    {
      "Action": "wechat_task",
      "Args": {
        "action": "text",
        "content": "已完成办公系统消息检查。"
      }
    }
  ]
}
```

---

## Coze 智能体接入

OpenTangYuan 支持作为 Coze 智能体的外部任务执行平台使用。

推荐在 Coze 智能体中配置专用系统提示词，用于约束智能体的技能调用顺序、参数格式、停止规则和副作用操作控制。

完整提示词建议放在：

```text
docs/coze-agent-prompt.md
```

### 核心调用原则

1. 先调用 `GetSkillListForAI` 查询可用技能。
2. 如返回 `needDetail=true`，再按类型查询技能详情：
   - Workflow：`GetSkillAction`
   - Builtin：`GetBuiltinSkillDetail`
3. 确认参数后调用 `ExecuteSkill` 或 `ExecuteSkillForCoze`。
4. 执行成功且目标完成后立即停止。
5. 列表查询成功后停在列表，只有用户明确要求“看详情 / 正文 / 第一条 / 第二条”才继续。
6. 对发送、回复、复制、移动、删除、下载附件、标记已读、保存文件等副作用动作严格限制重复执行。
7. 同一 Skill 失败时，只允许修正参数后最多重试一次。
8. 缺少必要参数时先询问用户，不要猜测参数。

### 参数格式约束

调用 `ExecuteSkill` 时，顶层只允许：

```json
{
  "SkillCode": "",
  "Arguments": {},
  "Steps": []
}
```

规则：

- 无参数时，`Arguments` 传 `{}`。
- 多步任务引用前一步结果时，使用 `step0`、`step1`、`step2`。
- 不要猜本地路径。
- `ExecuteSkillForCoze` 使用时，将 `{ skillCode, arguments }` 序列化后放进 `Json` 字段。

### 邮件任务约束

`email_task` 查询列表时应先使用 `search`。

列表查询成功后默认停止，只有用户明确要求时才继续执行：

- `read`
- `download_attachments`
- `reply`
- `mark_read`
- `save_eml`

发邮件时：

- 普通附件使用 `attachments`
- 图片默认插入正文
- 正文图片使用 `insertImagePaths`
- 不要自行拼接 `cid`

### 截图任务约束

- 网页截图使用 `browser_task`
- 本地界面截图使用 `open_task` + `screenshot_task`
- 不要混用网页截图和本地截图能力

---

## 响应格式

### 成功响应

```json
{
  "success": true,
  "message": "执行成功",
  "data": {}
}
```

### 失败响应

```json
{
  "success": false,
  "message": "邮箱未配置",
  "errorCode": "EMAIL_CONFIG_MISSING",
  "data": null
}
```

### 常见错误码

| 错误码 | 说明 |
|---|---|
| `SKILL_NOT_FOUND` | 技能不存在 |
| `INVALID_ARGUMENTS` | 参数不合法 |
| `EMAIL_CONFIG_MISSING` | 邮箱配置缺失 |
| `FILE_NOT_FOUND` | 文件不存在 |
| `EXECUTION_FAILED` | 技能执行失败 |
| `SIDE_EFFECT_BLOCKED` | 副作用操作被阻止重复执行 |
| `PERMISSION_DENIED` | 权限不足 |
| `TIMEOUT` | 执行超时 |

> 请根据实际后端返回格式调整本节内容。

---

## 安全说明

OpenTangYuan 可执行邮件发送、文件操作、打印、浏览器访问等具有副作用的任务。请在受信任环境中运行，并妥善配置访问权限。

建议：

- 不要将邮箱密码、授权码、Token 提交到代码仓库。
- 生产环境请限制 API 访问来源。
- 不建议将服务直接暴露到公网。
- 对发送邮件、删除文件、移动文件、打印等操作启用审计日志。
- 对敏感操作增加二次确认机制。
- 使用最小权限原则配置系统账号和邮箱账号。
- 定期轮换邮箱授权码、企业微信 Webhook 等密钥。
- 对内网系统访问进行白名单控制。
- 对技能执行结果进行日志记录，便于审计和问题追踪。

### 数据安全原则

OpenTangYuan 推荐采用“云端规划、本地执行”的模式：

| 原则 | 说明 |
|---|---|
| 云端不存储敏感数据 | 云端智能体只负责任务规划和参数生成 |
| 本地执行真实操作 | 文件、邮件、浏览器、内网系统等操作在本地可信环境执行 |
| 最小权限访问 | 每个 Skill 只获取完成任务所需的最小权限 |
| 执行可追踪 | 所有关键执行动作建议记录日志 |
| 副作用可控制 | 发送、删除、移动、打印等操作应避免重复执行 |

---

## 文档导航

建议将详细内容拆分到 `docs` 目录中维护。

| 文档 | 说明 |
|---|---|
| `docs/api.md` | API 接口说明 |
| `docs/workflow.md` | Workflow 开发指南 |
| `docs/builtins.md` | 内置技能说明 |
| `docs/deployment.md` | 部署指南 |
| `docs/coze-agent-prompt.md` | Coze 智能体系统提示词 |
| `docs/architecture.md` | 系统架构说明 |
| `docs/faq.md` | 常见问题 |

---

## 推荐仓库结构

```text
OpenTangYuan
├── README.md
├── LICENSE
├── CHANGELOG.md
├── CONTRIBUTING.md
├── CODE_OF_CONDUCT.md
├── SECURITY.md
├── .gitignore
│
├── docs
│   ├── api.md
│   ├── workflow.md
│   ├── deployment.md
│   ├── coze-agent-prompt.md
│   ├── builtins.md
│   ├── architecture.md
│   └── faq.md
│
├── readme
│   ├── logo.png
│   ├── demo-1.png
│   ├── demo-2.png
│   ├── architecture.png
│   └── flowchart.png
│
├── src
│
└── tests
```

---

## 开发与贡献

欢迎提交 Issue 和 Pull Request。

### 本地开发环境

推荐使用：

- Visual Studio 2022
- JetBrains Rider
- VS Code + C# Dev Kit
- dotnet CLI

### 安装常用依赖

```bash
dotnet add package MailKit
dotnet add package Microsoft.Playwright
dotnet add package Microsoft.Data.Sqlite
```

### 运行测试

```bash
dotnet test
```

### 提交规范

提交信息建议使用以下格式：

```text
<type>: <subject>
```

示例：

```text
feat: 添加企业微信卡片消息
fix: 修复邮件附件下载失败问题
docs: 更新 README 使用示例
```

常见 type：

| 类型 | 说明 |
|---|---|
| `feat` | 新功能 |
| `fix` | 修复问题 |
| `docs` | 文档更新 |
| `refactor` | 代码重构 |
| `test` | 测试相关 |
| `chore` | 构建、依赖或其他杂项 |

### Pull Request 建议

- 新功能请尽量包含测试用例。
- 重大变更请先提交 Issue 讨论。
- 提交前请确保项目可以正常构建和测试。
- 请尽量保持 PR 范围清晰，避免一次提交包含过多无关变更。

---

## 路线图

- [ ] Workflow 可视化编排器
- [ ] Web 管理后台
- [ ] 插件化 Skill 扩展机制
- [ ] 权限管理与操作审计
- [ ] Docker Compose 部署支持
- [ ] 多模型接入
- [ ] MCP 支持
- [ ] 更多办公软件自动化能力
- [ ] 技能市场 / 插件市场
- [ ] 更完善的执行日志与追踪能力
- [ ] 企业级用户与角色权限体系
- [ ] 分布式 Runtime 节点管理

---

## 常见问题

### 1. 为什么邮件发送失败？

请检查：

- SMTP 配置是否正确
- 是否使用了邮箱客户端授权码
- SMTP SSL 配置是否正确
- 邮箱服务商是否开启了 SMTP 服务
- 当前网络是否允许访问 SMTP 服务器

### 2. 为什么读取邮件失败？

请检查：

- IMAP 配置是否正确
- 邮箱是否开启 IMAP 服务
- 授权码是否有效
- 邮箱服务商是否限制第三方客户端登录

### 3. 为什么文件无法打开或打印？

请检查：

- 文件路径是否存在
- 当前运行账号是否有访问权限
- 当前系统是否安装了对应文件类型的默认打开程序
- 打印机是否可用

### 4. 为什么浏览器任务失败？

请检查：

- Playwright 是否安装完整
- 目标网页是否需要登录
- 当前运行环境是否支持浏览器启动
- 页面选择器是否正确

### 5. 为什么多步任务引用不到上一步结果？

请检查：

- 步骤编号是否正确，例如 `step0`、`step1`
- 返回字段路径是否正确，例如 `{{step0.data.path}}`
- 前一步是否执行成功
- 不要手动猜测文件路径，应使用前一步返回结果

---

## 社区与支持

项目地址：

```text
https://gitee.com/l00f/open-tang-yuan

https://github.com/wrnas/OpenTangYuan

```

欢迎通过以下方式参与项目：

- 提交 Issue
- 提交 Pull Request
- 完善文档
- 反馈使用场景
- 分享自动化 Workflow 示例
- 提供企业或校园办公自动化场景建议

---

## 致谢

感谢所有贡献者、使用者和反馈者。

---

## 许可证

本项目采用 MIT 许可证。

详见 [LICENSE](LICENSE)。
