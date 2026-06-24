# OpenTangYuan

<p align="center">
  <strong>面向隐私敏感办公自动化的云端—本地 Agent Workflow Runtime</strong>
</p>

<p align="center">
  通过外部智能体规划、本地可信执行、能力发现和 Workflow Runtime，连接文件、浏览器、邮件、截图、企业消息和本地工具。
</p>

<p align="center">
  <a href="#项目概览">项目概览</a> ·
  <a href="#快速开始">快速开始</a> ·
  <a href="#核心机制">核心机制</a> ·
  <a href="#系统架构">系统架构</a> ·
  <a href="#示例-workflow">示例 Workflow</a> ·
  <a href="#安全与部署边界">安全与部署边界</a> ·
  <a href="#引用方式">引用方式</a>
</p>

<p align="center">
  <a href="#"><img src="https://img.shields.io/badge/.NET-8.0-purple" alt=".NET 8"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green" alt="MIT License"></a>
  <a href="#"><img src="https://img.shields.io/badge/platform-Windows%20%7C%20Docker-blue" alt="Platform"></a>
  <a href="#"><img src="https://img.shields.io/badge/status-research%20software-orange" alt="Research Software"></a>
  <a href="#"><img src="https://img.shields.io/badge/Agent-Workflow%20Runtime-blueviolet" alt="Agent Workflow Runtime"></a>
</p>

---

## 项目概览

**OpenTangYuan** 是一个面向隐私敏感办公自动化场景的开源 **Agent Workflow Runtime**。它将外部大语言模型智能体的自然语言理解、任务规划和技能选择能力，与本地可信运行时中的文件、浏览器、邮件、截图、企业消息和本地工具调用能力连接起来。

OpenTangYuan 的核心目标是：

- 让外部智能体能够发现并调用本地能力；
- 将真实文件、邮件、截图和内部系统访问保留在本地可信环境；
- 支持预定义 Workflow 的复用，也支持临时多步骤任务的动态组合；
- 通过认证、白名单、策略校验和执行日志降低本地自动化的副作用风险。

本项目不是单一聊天机器人，也不是简单的 tool-calling 示例。它面向实际办公自动化任务，强调 **cloud-side planning + trusted local execution** 的执行边界。

---

## 适用场景

OpenTangYuan 适用于需要跨系统、重复性、隐私敏感和本地执行能力的办公自动化任务，例如：

- 搜索本地文件、复制文件、打开文档或整理目录；
- 读取邮件、下载附件、发送邮件或将截图插入邮件正文；
- 打开浏览器页面、提取网页内容、截图或下载文件；
- 调用本地工具或白名单中的可执行程序；
- 向企业微信、钉钉等企业消息平台推送结果；
- 将多个本地技能组合成可复用 Workflow；
- 通过 Coze、Dify、GPTs 或自定义 agent 平台触发本地任务。

当前验证场景来自高校行政办公，但系统设计并不依赖特定学校或业务系统，也可迁移到科研管理、企业办公、实验室管理和其他隐私敏感机构场景。

---

## 核心特性

### 1. Manifest-driven capability discovery

OpenTangYuan 使用 `skill-manifest.json` 描述本地技能能力、参数、动作和调用示例。外部智能体不需要一次性携带全部工具说明，而是按需查询可用 Workflow 与 Builtin Skill。

典型调用链路：

```text
GetSkillListForAI
        ↓
GetBuiltinSkillDetail / GetSkillAction
        ↓
ExecuteSkill / ExecuteSkillForCoze
```

### 2. Workflow-first execution

系统优先复用数据库中已验证的多步骤 Workflow。对于没有匹配 Workflow 的临时任务，智能体可以查询 Builtin Skill 的参数说明，并组合为 temporary workflow。

这种策略适合高频办公任务：

- 高频任务复用 Workflow，减少重复规划；
- 临时任务使用 Builtin Skill 兜底；
- 中间结果写入上下文，后续步骤可通过模板变量引用。

### 3. Trusted local runtime

真实执行发生在本地可信运行时中，包括文件、邮件、浏览器、截图、本地工具和企业系统访问。外部智能体只获得能力元数据和结构化执行结果，不直接访问敏感资源。

### 4. Policy-controlled side effects

OpenTangYuan 对具有副作用的操作进行策略约束，包括：

- 发送邮件；
- 回复邮件；
- 复制、移动或删除文件；
- 下载附件；
- 打开本地程序；
- 打印文件；
- 调用外部工具。

系统支持 API 认证、路径白名单、可执行程序白名单、副作用操作控制和执行日志。

---

## 系统架构

![Architecture](readme/images/architecture.png)

OpenTangYuan 采用云端—本地协同架构，主要包括：

| 层级 | 说明 |
|---|---|
| User Interaction Layer | 用户入口，包括 Web、移动端、聊天入口、API、SDK、Coze、Dify、GPTs、自定义 agent 等 |
| OpenTangYuan Orchestration Layer | 维护 Workflow Repository 和 Skill Registry，支持能力发现、任务规划和技能路由 |
| Secure Execution Channel | 负责云端编排层与本地执行层之间的安全通信 |
| Trusted Local Runtime Layer | 负责认证、策略校验、Workflow 调度、Skill 调用、上下文管理和结果封装 |
| Enterprise Integration Layer | 对接浏览器、邮件、文件系统、企业微信、本地工具、OA、ERP/CRM、自定义 API 等 |
| Governance, Security & Compliance | 负责隐私保护、访问控制、执行审计、监控告警等 |

核心边界：

```text
Cloud side: planning and capability metadata only
Local runtime: execution and sensitive data processing
Enterprise systems: accessed only through trusted local runtime
```

---

## Capability Discovery

![Capability Discovery](readme/images/capability-discovery.png)

外部智能体通过能力发现接口了解当前系统可用能力：

1. 调用 `GetSkillListForAI` 获取 Workflow 与 Builtin Skill 摘要；
2. 如果某项能力需要详情，则调用 `GetBuiltinSkillDetail` 或 `GetSkillAction`；
3. 根据返回的参数说明构造调用请求；
4. 通过 `ExecuteSkill` 或 `ExecuteSkillForCoze` 提交执行。

### 设计优势

| 机制 | 作用 |
|---|---|
| 能力摘要查询 | 避免智能体一次性加载全部工具说明 |
| 按需查询详情 | 降低提示词长度和调用复杂度 |
| Workflow 优先 | 高频任务直接复用，提升一致性 |
| Builtin Skill 兜底 | 支持临时任务动态组合 |
| Manifest 驱动 | 便于扩展本地能力和维护参数说明 |

---

## Workflow Runtime

OpenTangYuan 内置 Workflow Runtime，用于执行预定义或临时生成的多步骤任务。

Workflow Runtime 支持：

- Step scheduling；
- Context propagation；
- Template variable resolution；
- Runtime execution；
- Result packaging；
- Debug log；
- Failure reporting。

### 执行流程

```text
1. 接收 Workflow Steps
2. 初始化执行上下文
3. 顺序执行每个 Step
4. 解析模板变量
5. 调用对应 Builtin Skill
6. 将结果写入 stepN
7. 下一步引用前一步结果
8. 返回最终执行结果
```

### 模板变量示例

```text
{{step0}}
{{step0.path}}
{{step0.data.path}}
{{step1.result}}
```

---

## 核心 API

README 仅列出智能体调用链路中最重要的接口。完整 API 请参见 [`docs/api.md`](docs/api.md)。

| API | 方法 | 路径 | 用途 |
|---|---|---|---|
| GetSkillListForAI | POST | `/api/Skills/GetSkillListForAI` | 获取 Workflow 与 Builtin Skill 摘要 |
| GetBuiltinSkillDetail | POST | `/api/Skills/GetBuiltinSkillDetail` | 获取某个 Builtin Skill 的详细定义 |
| GetBuiltinSkillManifest | POST | `/api/Skills/GetBuiltinSkillManifest` | 获取完整 Builtin Skill Manifest |
| GetSkillAction | POST | `/api/Skills/GetSkillAction` | 获取某个 Workflow 的步骤定义 |
| ExecuteSkill | POST | `/api/Skills/ExecuteSkill` | 统一执行 Builtin Skill、Workflow 或临时任务 |
| ExecuteSkillForCoze | POST | `/api/Skills/ExecuteSkillForCoze` | Coze 兼容执行入口 |

### 获取能力列表

```http
POST /api/Skills/GetSkillListForAI
```

示例：

```bash
curl -X POST http://localhost:54124/api/Skills/GetSkillListForAI
```

### 统一执行入口

```http
POST /api/Skills/ExecuteSkill
```

请求字段：

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `SkillCode` | string | 是 | 技能标识，例如 `email_task`、`temp_task` |
| `Arguments` | object | 否 | 单步任务参数 |
| `Steps` | array | 否 | 临时 Workflow 步骤 |

### Swagger / OpenAPI

![Swagger](readme/images/swagger-1.png)

服务启动后，可通过 Swagger 查看和测试 API。具体地址请以项目启动日志为准，常见形式为：

```text
http://localhost:54124/swagger
```

---

## 快速开始

### 环境要求

完整桌面自动化能力主要面向 Windows 环境。

推荐环境：

- Windows 10 / Windows 11 / Windows Server 2016+
- .NET 8 SDK 或 .NET 8 Runtime
- Visual Studio 2022、JetBrains Rider 或 dotnet CLI
- SQLite
- 可选：邮箱账号、企业微信机器人、本地浏览器环境

> 说明：得益于 .NET 8 的跨平台能力，服务端组件可在多个操作系统中运行；但本地文件搜索、桌面文档打开、截图、本地工具调用等完整桌面自动化能力依赖 Windows 桌面资源。项目提供 Windows self-contained release，用户无需预先安装 .NET 8 Runtime 即可启动本地运行时。

### 克隆项目

```bash
git clone https://github.com/wrnas/OpenTangYuan.git
cd OpenTangYuan
```

也可以使用 Gitee 镜像：

```bash
git clone https://gitee.com/l00f/open-tang-yuan.git
cd open-tang-yuan
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

如果服务正常，将返回当前可用的 Workflow 和 Builtin Skill 摘要。

---

## Docker 部署

Docker 部署适合服务器端组件和 API 检查。涉及桌面资源的本地能力，如桌面截图、打开文档、Windows 文件搜索等，仍需在支持桌面环境的本地 Runtime 中运行。

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

### docker-compose 示例

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

## 配置说明

生产环境不要把邮箱授权码、Webhook Key、API Token、内网系统账号或数据库密钥提交到代码仓库。建议使用环境变量、用户机密配置、Docker Secret、CI/CD Secret 或独立生产配置文件。

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

### 外部程序白名单示例

```json
{
  "AllowedExeNames": [
    "pandoc.exe",
    "custom-tool.exe"
  ]
}
```

---

## 内置技能

| SkillCode | 功能 |
|---|---|
| `email_task` | 发送邮件、搜索邮件、读取正文、下载附件、回复邮件、标记已读、保存 eml |
| `wechat_task` | 发送企业微信 text、markdown、card 消息 |
| `browser_task` | 浏览器自动化、网页访问、截图、内容提取、文件下载 |
| `file_task` | 文件搜索、复制、移动、重命名、创建目录、批量操作 |
| `open_task` | 打开本地文件、目录或程序 |
| `print_task` | 打印本地文件 |
| `tool_task` | 调用白名单中的本地工具或可执行程序 |
| `screenshot_task` | 截取本地屏幕 |
| `folder_task` | 按后缀归类文件 |
| `lock_task` | 锁定本地工作站 |

---

## 示例 Workflow

### 示例 1：截图并发送邮件

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
        "body": "以下是自动截取的屏幕截图。",
        "insertImagePaths": [
          "{{step0.data.path}}"
        ]
      }
    }
  ]
}
```

### 示例 2：文件搜索、打开、截图并发送邮件

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

> 注：示例中的字段名应与当前版本后端返回结构保持一致。实际部署时，请先通过 Swagger 或调试日志确认 `firstPath`、`data.path` 等字段。

---

## Coze / 外部智能体接入

OpenTangYuan 可作为 Coze、Dify、GPTs 或自定义 agent 平台的外部执行 Runtime。外部智能体负责理解用户请求、选择技能并构造结构化任务，本地 Runtime 负责真实执行。

推荐调用原则：

1. 先调用 `GetSkillListForAI` 查询可用能力；
2. 如返回 `needDetail=true`，再查询详细定义：
   - Workflow：调用 `GetSkillAction`；
   - Builtin Skill：调用 `GetBuiltinSkillDetail`；
3. 确认参数后调用 `ExecuteSkill` 或 `ExecuteSkillForCoze`；
4. 任务完成后立即停止，不重复执行副作用操作；
5. 缺少必要参数时先询问用户，不猜测路径、邮箱、文件名或系统账号；
6. 同一 Skill 失败时，只允许修正参数后有限重试。

### Coze 配置示例

![Coze Agent Config](readme/images/coze-agent-config.png)

### Coze 调试轨迹

![Coze Trace](readme/images/coze-trace.png)

---

## 运行示例

### 智能体运行示例

![Demo 1](readme/images/demo-1.png)

### 动态组合任务执行过程

![Demo 2](readme/images/demo-2.png)

---

## 安全与部署边界

OpenTangYuan 可以执行邮件发送、文件操作、浏览器访问、截图、打印和本地程序调用等具有副作用的任务，因此应部署在受信任环境中，并配置访问控制。

### Cloud-local boundary

| 边界 | 说明 |
|---|---|
| 外部智能体 | 负责任务理解、Workflow 规划和技能路由 |
| Trusted Local Runtime | 负责真实执行、认证、策略校验、上下文管理和结果封装 |
| 企业系统 | 仅由本地 Runtime 访问，不直接暴露给外部智能体 |

### 安全建议

- 不要将邮箱密码、授权码、Token 或 Webhook Key 提交到仓库；
- 生产环境应限制 API 访问来源；
- 不建议将本地 Runtime 直接暴露到公网；
- 对发送邮件、删除文件、移动文件、打印等操作启用审计日志；
- 对高风险操作增加人工确认或审批；
- 使用路径白名单限制文件系统访问；
- 使用可执行程序白名单限制本地工具调用；
- 定期轮换邮箱授权码、企业微信 Webhook 和 API Token。

---

## 平台与复现说明

OpenTangYuan 的完整桌面自动化能力主要面向 Windows，因为本地文件搜索、桌面文档打开、截图和本地工具调用依赖 Windows 桌面资源。

为降低复现门槛，项目提供以下验证路径：

| 验证方式 | 平台 | 说明 |
|---|---|---|
| 查看源码与文档 | 任意平台 | 适合了解架构、API、Workflow 和安全模型 |
| 启动服务并查看 Swagger | Windows / Linux / Docker | 可验证 API 和能力发现接口 |
| Windows self-contained release | Windows | 无需预装 .NET 8 Runtime，可启动本地 Runtime |
| 完整外部智能体集成 | Windows Runtime + 外部 agent 平台 | 需要配置 Coze/Dify/GPTs、自定义 agent、网络连通、API 认证和本地邮箱/浏览器环境 |

真实试点日志可能包含邮件内容、文件路径、截图、办公系统内容和用户操作信息，因此不公开原始日志。仓库提供代码、文档、示例 Workflow 和部署说明，用于复现软件结构和运行路径。

---

## 文档目录

建议仓库包含以下文档：

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

其中：

| 文件 | 说明 |
|---|---|
| `docs/api.md` | API 参数、请求和响应说明 |
| `docs/workflow.md` | Workflow 定义、上下文变量和执行机制 |
| `docs/deployment.md` | 本地、内网、Docker 和私有云部署方式 |
| `docs/security.md` | 安全边界、白名单、认证和副作用控制 |
| `docs/coze-agent-prompt.md` | 外部智能体系统提示词建议 |
| `docs/examples.md` | 示例请求和示例 Workflow |
| `docs/faq.md` | 常见问题 |

---

## 开发与贡献

欢迎提交 Issue 和 Pull Request。建议开发环境：

- Visual Studio 2022；
- JetBrains Rider；
- VS Code + C# Dev Kit；
- dotnet CLI。

### 常见依赖

```bash
dotnet add package MailKit
dotnet add package Microsoft.Playwright
dotnet add package Microsoft.Data.Sqlite
dotnet add package Dapper
```

### 提交信息建议

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

## 路线图

- [ ] Workflow 可视化编排器
- [ ] Web 管理后台
- [ ] 插件化 Skill 扩展机制
- [ ] 权限管理与操作审计
- [ ] 更完善的 Docker Compose 部署
- [ ] GitHub Release
- [ ] Zenodo DOI
- [ ] MCP 支持
- [ ] 更多办公软件自动化能力
- [ ] 自动化测试与基准任务集
- [ ] 分布式 Runtime 节点管理

---

## 常见问题

### 为什么邮件发送失败？

请检查 SMTP 配置、SSL 设置、邮箱客户端授权码、邮箱服务商 SMTP 开关和当前网络环境。

### 为什么读取邮件失败？

请检查 IMAP 配置、授权码、邮箱服务商第三方客户端设置和网络访问权限。

### 为什么文件无法打开或打印？

请检查文件路径是否存在、运行账号是否有权限、系统是否安装默认打开程序，以及路径是否位于 `AllowedRoots` 白名单中。

### 为什么浏览器任务失败？

请检查 Playwright 是否安装完整、目标网页是否需要登录、页面选择器是否正确，以及是否遇到验证码或多因素认证。

### 为什么多步任务引用不到上一步结果？

请检查步骤编号、返回字段路径、前一步是否执行成功，以及模板变量是否与实际返回结构一致。

---

## 引用方式

如果你在研究或项目中使用 OpenTangYuan，请引用本项目。论文正式发表后，请替换为 SoftwareX 论文引用格式。

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

## 许可证

本项目采用 MIT License。详见 [LICENSE](LICENSE)。

---

## 致谢

感谢所有参与 OpenTangYuan 设计、开发、测试和应用反馈的同事、用户和贡献者。
