# OpenTangYuan

<p align="center">
  <strong>面向隐私敏感办公自动化的云端—本地 Agent Workflow Runtime</strong>
</p>

<p align="center">
  通过云端规划与本地可信执行，连接浏览器、邮件、文件系统、企业消息、本地工具和内部系统。
</p>

<p align="center">
  <a href="#opentangyuan-是什么">项目简介</a> ·
  <a href="#它能做什么">能做什么</a> ·
  <a href="#快速开始">快速开始</a> ·
  <a href="#核心概念">核心概念</a> ·
  <a href="#workflow-runtime">Workflow Runtime</a> ·
  <a href="#核心-api">核心 API</a> ·
  <a href="#安全与部署边界">安全边界</a> ·
  <a href="#研究背景">研究背景</a> ·
  <a href="#引用方式">引用方式</a>
</p>

<p align="center">
  <a href="#"><img src="https://img.shields.io/badge/.NET-8.0-purple" alt=".NET 8"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green" alt="MIT License"></a>
  <a href="#"><img src="https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20Docker-blue" alt="Platform"></a>
  <a href="#"><img src="https://img.shields.io/badge/status-research%20software-orange" alt="Research Software"></a>
  <a href="#"><img src="https://img.shields.io/badge/Agent-Workflow%20Runtime-blueviolet" alt="Agent Workflow Runtime"></a>
</p>

---

## OpenTangYuan 是什么？

**OpenTangYuan** 是一个开源的 **Agent Workflow Runtime**，面向隐私敏感的办公自动化和机构工作流自动化场景。

它解决的是一个很常见的问题：外部 AI 智能体很擅长理解用户需求、拆解任务、生成计划，但真正执行任务时，经常需要访问本地文件、邮箱、浏览器、截图、企业消息工具或内部业务系统。这些资源通常不适合直接暴露给云端智能体。

OpenTangYuan 把这两部分分开：

```text
Cloud side: planning and capability metadata
Local runtime: execution and sensitive data processing
Enterprise systems: accessed only through the trusted local runtime
```

也就是说，智能体可以先发现本地有哪些能力，再按需查询能力详情，组合出一个 workflow，并把任务提交给本地可信运行时。本地运行时负责真正执行操作，并返回结构化结果。

OpenTangYuan 不是一个单独的聊天机器人，也不是一个简单的 tool-calling demo。它更像一个运行时层，让外部智能体可以安全地使用本地自动化能力，同时把敏感数据和执行权限留在本地控制范围内。

最初的验证场景来自高校行政办公，但项目设计并不绑定某个学校或某套业务系统。它也可以用于科研管理、企业后台办公、实验室管理、政务辅助办公，以及其他需要隐私保护的自动化场景。

---

## 它能做什么？

OpenTangYuan 适合那些需要跨系统、可重复执行、并且必须靠近私有数据运行的任务。常见例子包括：

- 搜索、复制、移动、重命名、打开或整理本地文件和文件夹；
- 搜索邮件、读取邮件、下载附件、回复邮件、发送邮件，以及把截图插入邮件正文；
- 自动化浏览器操作，包括打开网页、提取内容、截图和下载文件；
- 将结果推送到企业微信、钉钉等企业消息平台；
- 调用白名单中的本地工具或可执行程序；
- 将多个本地能力组合成可复用 workflow；
- 从 Coze、Dify、GPTs 或自定义 agent gateway 触发本地任务。

简单任务可以只调用一个内置技能，例如搜索邮件。复杂任务可以由多个步骤组成，例如：

```text
search file -> open it -> capture screenshot -> send email
```

---

## 快速开始

### 环境要求

完整桌面自动化能力推荐在 Windows 10、Windows 11 或 Windows Server 2016+ 上运行。你需要准备：

- .NET 8 SDK 或 Runtime；
- Visual Studio 2022、JetBrains Rider，或 dotnet CLI；
- SQLite；
- 可选：邮箱账号、企业微信机器人、本地浏览器环境。

得益于 .NET 8 的跨平台能力，服务端组件可以在 Linux 或 Docker 中运行。不过，桌面文件搜索、打开文档、截图、本地工具调用等能力依赖 Windows 桌面资源。

### 克隆仓库

```bash
git clone https://github.com/wrnas/OpenTangYuan.git
cd OpenTangYuan
```

也可以使用 Gitee：

```bash
git clone https://gitee.com/l00f/open-tang-yuan.git
cd open-tang-yuan
```

### 恢复依赖

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

如果服务正常，你会看到当前可用的 workflows 和 built-in skills 列表。

### Swagger / OpenAPI

![Swagger](readme/images/swagger-1.png)

服务启动后，可以访问：

```text
http://localhost:54124/swagger
```

通过 Swagger 查看和测试接口。

---

## 核心概念

### 1. Agent workflow runtime

OpenTangYuan 不只是一个 tool-calling 层。它是一个面向智能体任务执行的 runtime，负责把“自然语言需求”落到“可执行的本地步骤”上。

它提供：

- 技能发现；
- 技能详情查询；
- 参数生成；
- 多步骤编排；
- 上下文传递；
- 本地可信执行；
- 结果返回；
- 调试与错误反馈。

### 2. Manifest-driven skill registry

所有内置技能都通过 `skill-manifest.json` 描述。智能体不需要预先记住每个技能的完整参数，而是按需发现和查询：

```text
GetSkillListForAI
        ↓
GetBuiltinSkillDetail / GetSkillAction
        ↓
ExecuteSkill / ExecuteSkillForCoze
```

这种方式可以减少 prompt 长度，提高调用稳定性，也方便后续扩展新的本地能力。

### 3. Workflow-first execution

OpenTangYuan 区分两类能力：

| 类型 | 说明 |
|---|---|
| Builtin Skill | 原子能力，例如邮件、文件、浏览器、截图、消息、本地工具 |
| Workflow Skill | 由多个 Builtin Skill 组成的可复用流程 |

运行时会优先复用数据库中已经验证过的 workflow。对于新的临时任务，智能体可以查询内置技能详情，并动态组合 temporary workflow。

### 4. Context-aware multi-step execution

后续步骤可以引用前面步骤的结果，例如：

```text
{{step0}}
{{step0.path}}
{{step0.data.path}}
{{step1.result}}
```

这样就可以把“搜索文件 → 打开文件 → 截图 → 发邮件”这类流程结构化地表达出来，并交给 runtime 执行。

### 5. Trusted local execution

真正的操作都发生在本地 runtime 中，包括文件、邮件、浏览器、截图、本地工具和企业系统访问。外部智能体只看到能力元数据和结构化结果，不直接接触敏感数据。

### 6. Policy-controlled side effects

系统会对具有副作用的操作进行约束和记录，例如：

- 发送或回复邮件；
- 复制、移动或删除文件；
- 下载附件；
- 启动本地程序；
- 打印文件；
- 调用外部工具。

配合 API 认证、路径白名单、可执行程序白名单和执行日志，可以搭建一个更安全的自动化环境。

---

## 系统架构

![Architecture](readme/images/architecture.png)

OpenTangYuan 采用云端—本地协同架构：

| 层级 | 职责 |
|---|---|
| User Interaction Layer | Web、移动端、聊天入口、API、SDK、Coze、Dify、GPTs、自定义 agent 等 |
| OpenTangYuan Orchestration Layer | Workflow repository、skill registry、能力发现、规划与路由 |
| Secure Execution Channel | 云端编排层与本地运行时之间的安全通信 |
| Trusted Local Runtime Layer | 认证、策略校验、workflow 调度、skill 调用、上下文管理和结果封装 |
| Enterprise Integration Layer | 浏览器、邮件、文件系统、企业微信、本地工具、OA、ERP/CRM、自定义 API 等 |
| Governance & Compliance | 隐私保护、访问控制、信任管理、执行审计、监控和告警 |

核心边界很清楚：

```text
Cloud side: planning and capability metadata only
Local runtime: execution and sensitive data processing
Enterprise systems: accessed only through trusted local runtime
```

---

## Capability Discovery

![Capability Discovery](readme/images/capability-discovery.png)

外部智能体通过一个简单的流程发现和调用能力：

1. 调用 `GetSkillListForAI`，获取可用 workflow 和 built-in skills 摘要；
2. 如果需要更多信息，则调用 `GetBuiltinSkillDetail`（内置技能）或 `GetSkillAction`（workflow）；
3. 根据返回的参数说明构造调用请求；
4. 通过 `ExecuteSkill` 或 `ExecuteSkillForCoze` 提交执行。

### 设计优势

| 机制 | 好处 |
|---|---|
| 初始只取摘要 | 减少 prompt 长度和 token 使用 |
| 按需查询详情 | 让智能体上下文更轻 |
| Workflow 优先 | 复用已验证流程，提高一致性 |
| Builtin skill 兜底 | 支持临时任务灵活组合 |
| Manifest 驱动 | 便于扩展和维护 |

---

## Workflow Runtime

OpenTangYuan 内置 workflow runtime，用于执行预定义或临时生成的多步骤任务。

它支持：

- Step scheduling；
- Context propagation；
- Template variable resolution；
- Runtime execution；
- Compact result packaging；
- Debug logging；
- Failure reporting。

### 执行流程

```text
1. 接收 workflow steps
2. 初始化执行上下文
3. 顺序执行每个 step
4. 解析模板变量
5. 调用对应 built-in skill
6. 将结果保存为 stepN
7. 后续 step 引用前面的结果
8. 返回最终结果
```

### 模板变量示例

```text
{{step0}}
{{step0.path}}
{{step0.data.path}}
{{step1.result}}
```

### 示例 workflow：截图并发送邮件

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
        "subject": "Screen capture",
        "body": "The automatically captured screenshot is attached below.",
        "insertImagePaths": [
          "{{step0.data.path}}"
        ]
      }
    }
  ]
}
```

### 示例 workflow：搜索、打开、截图并发送邮件

```json
{
  "SkillCode": "temp_task",
  "Arguments": {},
  "Steps": [
    {
      "Action": "file_task",
      "Args": {
        "action": "search",
        "keyword": "target_document",
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
      "Args": {
        "action": "capture_full_screen"
      }
    },
    {
      "Action": "email_task",
      "Args": {
        "action": "send",
        "to": "someone@example.com",
        "subject": "Document screenshot and attachment",
        "body": "The document screenshot is inserted below, and the original file is attached.",
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

---

## 核心 API

README 只列出智能体接入最常用的 API。完整参数请参考 `docs/api.md`。

| API | 方法 | 路径 | 用途 |
|---|---|---|---|
| GetSkillListForAI | POST | `/api/Skills/GetSkillListForAI` | 获取 workflow 和 built-in skill 摘要 |
| GetBuiltinSkillDetail | POST | `/api/Skills/GetBuiltinSkillDetail` | 获取某个 built-in skill 的详细定义 |
| GetBuiltinSkillManifest | POST | `/api/Skills/GetBuiltinSkillManifest` | 获取 built-in skill 的完整 manifest |
| GetSkillAction | POST | `/api/Skills/GetSkillAction` | 获取某个 workflow 的步骤定义 |
| ExecuteSkill | POST | `/api/Skills/ExecuteSkill` | 统一执行 built-in、workflow 或临时任务 |
| ExecuteSkillForCoze | POST | `/api/Skills/ExecuteSkillForCoze` | Coze 兼容执行入口 |

### 获取技能列表

```http
POST /api/Skills/GetSkillListForAI
```

示例：

```bash
curl -X POST http://localhost:54124/api/Skills/GetSkillListForAI
```

返回示例：

```json
{
  "success": true,
  "data": {
    "workflows": [
      {
        "skillCode": "capture_and_send_email",
        "AIDesc": "Capture a screenshot and send it by email.",
        "sourceType": "workflow",
        "needDetail": true
      }
    ],
    "builtins": [
      {
        "skillCode": "email_task",
        "AIDesc": "Email operations such as search, read, send, reply and attachment download.",
        "sourceType": "builtin",
        "needDetail": true
      }
    ]
  }
}
```

### 获取 built-in skill 详情

```http
POST /api/Skills/GetBuiltinSkillDetail
```

请求体：

```json
{
  "skillCode": "email_task"
}
```

### 获取 workflow 定义

```http
POST /api/Skills/GetSkillAction
```

请求体：

```json
{
  "skillCode": "capture_and_send_email"
}
```

### 执行 skill

```http
POST /api/Skills/ExecuteSkill
```

请求字段：

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| SkillCode | string | 是 | 例如 `email_task` 或 `temp_task` |
| Arguments | object | 否 | 单步任务参数 |
| Steps | array | 否 | temporary workflow 的步骤 |

### Coze 兼容执行

```http
POST /api/Skills/ExecuteSkillForCoze
```

请求体：

```json
{
  "Json": "{\"skillCode\":\"email_task\",\"arguments\":{\"action\":\"search\",\"subjectKeyword\":\"notification\",\"maxCount\":10}}"
}
```

### 响应格式

成功：

```json
{
  "success": true,
  "message": "Execution succeeded.",
  "data": {}
}
```

失败：

```json
{
  "success": false,
  "message": "Invalid arguments.",
  "errorCode": "INVALID_ARGUMENTS",
  "data": null
}
```

常见错误码：

| 错误码 | 说明 |
|---|---|
| SKILL_NOT_FOUND | 技能不存在 |
| INVALID_ARGUMENTS | 参数格式错误 |
| MISSING_ARGUMENTS | 缺少必要参数 |
| EMAIL_CONFIG_MISSING | 邮箱配置缺失 |
| FILE_NOT_FOUND | 文件不存在 |
| EXECUTION_FAILED | 技能执行失败 |
| SIDE_EFFECT_BLOCKED | 副作用操作被阻止（重复或不安全） |
| PERMISSION_DENIED | 权限不足 |
| TIMEOUT | 执行超时 |

---

## 技术栈

| 技术 | 用途 |
|---|---|
| .NET 8 | 后端运行框架 |
| C# 10+ | 主要开发语言 |
| ASP.NET WebAPI | 本地 runtime API |
| SQLite | Workflow 存储 |
| Dapper | 数据访问 |
| MailKit | SMTP / IMAP 邮件处理 |
| Playwright | 浏览器自动化 |
| Everything SDK / Windows Search | Windows 文件搜索 |
| WeChat Work Webhook / API | 企业消息通知 |
| REST API | 技能查询与执行接口 |
| JSON Manifest | 技能元数据描述 |
| Docker | 可选容器化部署 |

---

## Docker 部署

Docker 适合运行服务端组件和检查 API。但截图、打开桌面文档、Windows 文件搜索等桌面能力，仍然需要带桌面资源的 Windows runtime。

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

### `docker-compose.yml` 示例

```yaml
version: '3.8'

services:
  tangyuan-app:
    build: .
    container_name: tangyuan-app
    restart: always
    ports:
      - "54124:54124"
    volumes:
      - ./sqlite-data:/app/data
    environment:
      - TZ=Asia/Shanghai
      - ASPNETCORE_URLS=http://*:54124
```

---

## 配置

不要把邮箱密码、授权码、Webhook key、API token 或数据库密钥提交到仓库。建议使用环境变量、用户机密配置、Docker secrets、CI/CD variables，或独立的生产配置文件。

### 邮件配置示例

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

### 文件访问白名单示例

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

### 可执行程序白名单示例

```json
{
  "AllowedExeNames": [
    "pandoc.exe",
    "custom-tool.exe"
  ]
}
```

### 配置项说明

| 配置项 | 是否必填 | 说明 |
|---|---|---|
| EmailSettings:SmtpServer | 可选 | SMTP 服务器地址 |
| EmailSettings:SmtpPort | 可选 | SMTP 端口 |
| EmailSettings:SenderEmail | 可选 | 发件人邮箱 |
| EmailSettings:SenderPassword | 可选 | 邮箱授权码 |
| EmailSettings:ImapServer | 可选 | IMAP 服务器地址 |
| ConnectionStrings:Sqlite | 是 | SQLite 连接字符串 |
| FileSystem:AllowedRoots | 建议配置 | runtime 可访问的目录 |
| AllowedExeNames | 建议配置 | 可启动的程序白名单 |
| DebugMode | 可选 | 启用详细日志 |

---

## 内置技能

| SkillCode | 说明 |
|---|---|
| `email_task` | 发送、搜索、读取邮件、下载附件、回复、标记已读、保存为 `.eml` |
| `wechat_task` | 向企业微信发送 text、markdown 或 card 消息 |
| `browser_task` | 浏览网页、截图、提取内容、下载文件 |
| `file_task` | 搜索、复制、移动、重命名、创建目录、批量操作 |
| `open_task` | 打开本地文件、目录或程序 |
| `print_task` | 打印本地文件 |
| `tool_task` | 调用白名单中的本地工具或可执行程序 |
| `screenshot_task` | 截取全屏或活动窗口 |
| `folder_task` | 按后缀整理文件 |
| `lock_task` | 锁定本地工作站 |

---

## 运行截图

### 智能体运行示例

![Agent Execution Example](readme/images/demo-1.png)

这些截图来自中文办公自动化环境。论文中可以加入英文图注或标注，用来解释界面、workflow 步骤和执行结果。Runtime API 和 workflow 定义本身不依赖具体语言。

### 动态组合任务执行过程

![Dynamic Composite Task Execution](readme/images/demo-2.png)

### Coze 调试轨迹

![Coze Debug Trace](readme/images/coze-trace.png)

---

## Coze / 外部智能体接入

OpenTangYuan 可以作为 Coze、Dify、GPTs 或自定义 agent 平台的外部执行 runtime。外部智能体负责理解用户意图、选择技能和构造参数；本地 runtime 负责真实执行。

推荐调用流程：

1. 调用 `GetSkillListForAI` 查看可用能力；
2. 如果 `needDetail` 为 true，则继续查询详情：
   - Workflow：`GetSkillAction`；
   - Built-in skill：`GetBuiltinSkillDetail`；
3. 参数确认后调用 `ExecuteSkill` 或 `ExecuteSkillForCoze`；
4. 成功后立即停止，不要重复执行有副作用的操作；
5. 如果返回列表，先停在列表展示；只有用户明确要求查看详情时才继续；
6. 缺少必要参数时应询问用户，不要猜测路径、邮箱地址、文件名或凭据；
7. 如果某个 skill 执行失败，可以修正参数后最多重试一次。

### Coze agent 配置

![Coze Agent Configuration](readme/images/coze-agent-config.png)

---

## 安全与部署边界

OpenTangYuan 可以发送邮件、修改文件、控制浏览器、截图、打印并运行本地可执行程序。因此，它应该运行在受信任环境中，并配置适当的访问控制。

### Cloud-local boundary

| 组件 | 角色 |
|---|---|
| External agent | 任务理解、workflow 规划、技能路由 |
| Trusted Local Runtime | 执行、认证、策略校验、上下文管理、结果封装 |
| Enterprise systems | 只由本地 runtime 访问，不直接暴露给云端智能体 |

### 安全建议

- 不要把密码、token、webhook key 提交到仓库；
- 生产环境中限制 API 访问，例如 IP 白名单或 VPN；
- 不建议把本地 runtime 直接暴露到公网；
- 对所有副作用操作启用审计日志，例如发送邮件、删除文件、打印等；
- 对高风险操作增加人工确认或审批；
- 使用路径白名单限制文件系统访问；
- 使用可执行程序白名单限制可启动程序；
- 定期轮换邮箱授权码、webhook key 和 API token；
- 对内部系统端点做白名单控制，并记录访问日志；
- 保留执行日志，便于排错和合规审计。

---

## 平台与复现

完整桌面自动化能力以 Windows 为主，因为文件搜索、打开文档、截图和本地工具调用依赖 Windows 桌面资源。

为了方便评估，可以通过几种方式验证项目：

| 验证方式 | 平台 | 可以验证什么 |
|---|---|---|
| 查看源码和文档 | 任意平台 | 架构、API 设计、workflow 模型、安全边界 |
| 启动服务并查看 Swagger | Windows / Linux / Docker | API 端点和能力发现 |
| Windows self-contained release | Windows | 无需安装 .NET 8 即可运行本地 runtime |
| 完整外部智能体集成 | Windows Runtime + agent 平台 | 与 Coze/Dify/GPTs 或自定义 agent 的端到端自动化 |

真实试点日志可能包含邮件内容、文件路径、截图和内部系统数据，因此不公开。仓库中的代码、文档、示例 workflow 和部署指南可用于复现软件结构和执行路径。

---

## 扩展机制

你可以通过几种方式扩展 OpenTangYuan：

- **新增 Builtin Skill**：在 WebAPI 中实现逻辑，并在 `skill-manifest.json` 中注册；
- **新增 Workflow**：通过数据库或管理 API 保存，将多个 built-in skills 组合为可复用流程；
- **接入新的企业系统**：通过浏览器自动化、REST API、本地工具或自定义插件对接 OA、ERP、CRM、文件服务器、邮件服务器等；
- **扩展安全策略**：增加路径白名单、API 访问控制、角色权限、审批流、审计日志和告警规则。

---

## 推荐文档结构

建议维护以下配套文档：

```text
docs/
  api.md
  workflow.md
  deployment.md
  security.md
  coze-agent-prompt.md
  examples.md
  faq.md
```

| 文件 | 内容 |
|---|---|
| `api.md` | 完整 API 参数、请求和响应 |
| `workflow.md` | Workflow 定义、上下文变量和执行机制 |
| `deployment.md` | 本地、内网、Docker 和私有云部署方式 |
| `security.md` | 安全边界、白名单、认证和副作用控制 |
| `coze-agent-prompt.md` | 外部智能体系统提示词建议 |
| `examples.md` | 更多示例请求和 workflow |
| `faq.md` | 常见问题 |

---

## 开发与贡献

欢迎提交 issue 和 pull request。推荐开发环境：

- Visual Studio 2022；
- JetBrains Rider；
- VS Code + C# Dev Kit；
- dotnet CLI。

常见依赖：

```bash
dotnet add package MailKit
dotnet add package Microsoft.Playwright
dotnet add package Microsoft.Data.Sqlite
dotnet add package Dapper
```

提交信息建议：

```text
<type>: <subject>
```

示例：

```text
feat: add enterprise message notification
fix: handle missing email attachment path
docs: update workflow examples
```

---

## Roadmap

- Visual workflow designer；
- Web admin dashboard；
- Plugin-based skill extension；
- Permission management and operation auditing；
- Improved Docker Compose setup；
- GitHub Release with CI/CD；
- Zenodo DOI；
- MCP support；
- More office software automation capabilities；
- Automated test suite and benchmark tasks；
- Distributed runtime node management。

---

## FAQ

### 为什么不能发送邮件？

请检查 SMTP 配置、SSL 设置、授权码、邮箱服务商是否允许 SMTP，以及当前网络是否可访问邮件服务器。

### 为什么不能读取邮件？

请检查 IMAP 配置、授权码、是否开启 IMAP，以及邮箱服务商是否限制第三方客户端登录。

### 为什么不能打开或打印文件？

请确认文件存在，runtime 有权限访问该路径，系统安装了对应文件类型的默认打开程序，并且路径位于 `AllowedRoots` 内。

### 为什么浏览器任务失败？

请确认 Playwright 安装完整，目标页面不需要额外登录，选择器正确，并且没有被验证码或多因素认证阻挡。

### 为什么后续步骤引用不到前一步结果？

请检查 step 编号（例如 `step0`、`step1`）、字段路径（例如 `{{step0.data.path}}`），并确认前一步执行成功。不要手动猜测路径，应该使用实际返回结构。

---

## 研究背景

OpenTangYuan 最初作为研究软件开发，用于探索隐私敏感办公自动化中的 cloud-local agent execution。它在 GitHub 上的 README 首先面向用户和开发者，但项目也有明确的研究背景。

和普通 tool-calling 架构相比，OpenTangYuan 更关注几个实际问题：

- 外部智能体如何在不知道全部本地工具细节的情况下发现能力；
- 多步骤办公任务如何通过 workflow 结构化、复用和追踪；
- 敏感文件、邮件和企业系统如何保留在本地可信环境中；
- 发送邮件、删除文件、打印等副作用操作如何被策略控制；
- 同一套 runtime 如何被 Coze、Dify、GPTs 或自定义 agent 平台复用。

如果将 OpenTangYuan 用于研究或论文复现，建议重点关注：

- `skill-manifest.json` 中的 manifest-driven skill registry；
- `GetSkillListForAI` / `GetBuiltinSkillDetail` / `GetSkillAction` / `ExecuteSkill` 调用链；
- workflow runtime 的上下文传递和模板变量解析；
- cloud-local boundary 以及本地可信执行模型；
- 安全策略、白名单和副作用控制机制。

---

## 引用方式

如果你在研究或项目中使用 OpenTangYuan，请引用本项目。SoftwareX 论文发表后，可以将下面条目替换为正式论文引用格式。

### BibTeX

```bibtex
@software{opentangyuan,
  title        = {OpenTangYuan: A cloud-local agent workflow runtime for privacy-sensitive office automation},
  author       = {Liu, Mingyang and Contributors},
  year         = {2026},
  url          = {https://github.com/wrnas/OpenTangYuan},
  license      = {MIT},
  version      = {v1.1.2}
}
```

---

## License

本项目采用 MIT License。详见 [LICENSE](LICENSE)。

---

## 致谢

感谢所有参与 OpenTangYuan 设计、开发、测试和反馈的朋友。
