[English](README.md)

<p align="center">


# <img src="logo.png" alt="OpenTangYuan Logo" width="36"> OpenTangYuan

<p align="center">
  <strong>面向隐私敏感办公自动化的云端规划—本地执行 Agent 工作流运行时</strong>
</p>

<p align="center">
  通过云端任务理解与规划、可信本地执行，将浏览器、邮箱、文件系统、企业消息、本地工具和内部系统连接起来。
</p>

<p align="center">
  <a href="#项目简介">项目简介</a> ·
  <a href="#为什么使用-opentangyuan">设计特点</a> ·
  <a href="#系统架构">系统架构</a> ·
  <a href="#快速开始">快速开始</a> ·
  <a href="#工作流示例">工作流示例</a> ·
  <a href="#文档">文档</a> ·
  <a href="#安全与平台边界">安全边界</a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-purple" alt=".NET 8">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green" alt="MIT License"></a>
  <img src="https://img.shields.io/badge/runtime-Windows--first-blue" alt="Windows-first runtime">
  <img src="https://img.shields.io/badge/API-REST%20%2F%20OpenAPI-blueviolet" alt="REST / OpenAPI">
</p>

---

## 项目简介

**OpenTangYuan** 是一个开源的 **Agent 工作流运行时**，面向需要访问本地文件、邮箱、浏览器、桌面应用、企业消息平台或内部系统的办公自动化场景。

许多外部 AI Agent 能够理解用户意图并规划任务，但实际执行往往需要接触敏感数据或拥有本地操作权限。OpenTangYuan 将这两部分分开：

```text
云端或外部 Agent：理解意图、发现能力、规划任务、生成参数
可信本地运行时：验证请求、执行技能、管理工作流上下文、返回结构化结果
企业与本地资源：仅由可信本地运行时访问
```

外部 Agent 可以按需查询本地可用能力，获取技能或工作流的参数说明，组合单步或多步任务，再将任务提交给本地运行时执行。敏感数据和实际执行权限保留在用户控制的环境中。

OpenTangYuan 本身不是聊天机器人，也不绑定某一种 Agent 平台。它提供的是一个可供 Coze、Dify、GPTs、自定义 Agent 网关或普通桌面客户端调用的执行层。

---

## 为什么使用 OpenTangYuan？

OpenTangYuan 重点解决真实自动化场景中的能力发现、多步执行、上下文传递和本地权限控制问题。

1. **清单驱动的技能注册**  
   本地技能通过结构化清单描述能力、参数、动作、约束和副作用。Agent 可以先获取摘要，再按需查询详细定义，而不必一次性加载全部工具说明。

2. **可复用与临时工作流并存**  
   系统既可以执行数据库中保存的工作流，也可以执行 Agent 在运行时生成的临时多步骤任务。

3. **可信本地执行**  
   文件、邮件、浏览器、截图、本地程序和企业系统操作均在本地运行时完成，外部 Agent 不直接访问这些资源。

4. **工作流上下文传递**  
   每一步的结果会写入执行上下文，后续步骤可通过 `{{step0.data.path}}` 等模板变量引用前序结果。

5. **可控的副作用操作**  
   文件修改、邮件发送、打印和程序启动等操作可结合路径白名单、程序白名单、认证、策略校验和执行日志进行控制。

6. **统一的发现与执行接口**  
   一组稳定的 REST API 支持技能发现、详情查询、工作流读取和统一执行，方便接入不同 Agent 或客户端。

---

## 主要能力

OpenTangYuan 适用于需要跨多个本地或企业系统执行的任务，例如：

- 搜索、复制、移动、重命名、打开和整理本地文件；
- 搜索、读取、回复和发送邮件，以及下载附件或在正文中插入截图；
- 打开网页、提取页面内容、截图和下载文件；
- 向企业微信等消息平台发送通知；
- 启动白名单中的本地工具或程序；
- 将多个内置技能组合为可复用工作流；
- 接收外部 Agent、桌面客户端或自定义网关提交的任务。

一个典型的多步骤任务可以是：

```text
搜索文件 → 打开文件 → 截取屏幕 → 将截图和原文件发送到邮箱
```

---

## 系统架构

![OpenTangYuan 系统架构](docs/images/architecture.png)

OpenTangYuan 采用云端或外部 Agent 与本地运行时协作的架构：

| 层级 | 主要职责 |
|---|---|
| 用户与 Agent 层 | Web、移动端、聊天界面、Coze、Dify、GPTs、自定义 Agent 或桌面客户端。 |
| 能力发现与编排层 | 工作流目录、技能清单、能力查询、参数生成和调用路由。 |
| 可信本地运行时 | 请求验证、策略检查、工作流调度、技能调用、上下文管理和结果封装。 |
| 本地与企业集成层 | 浏览器、邮箱、文件系统、企业消息、本地程序、OA、ERP/CRM 和自定义接口。 |
| 治理与运维 | 访问控制、日志、审计、监控、告警和敏感配置管理。 |

完整设计说明见 [架构与运行机制](docs/architecture_zh-CN.md)。

---

## 快速开始

### 环境要求

完整桌面自动化建议运行在 Windows 10、Windows 11 或 Windows Server 2016 及以上版本。

基础要求：

- .NET 8 SDK 或 Runtime；
- Visual Studio 2022、JetBrains Rider、VS Code 或 `dotnet` CLI；
- SQLite；
- 按需配置邮箱、企业消息 Webhook、浏览器或本地工具。

服务端 API 可在 Linux 或 Docker 中运行，但桌面文件搜索、打开文档、屏幕截图和本地程序调用仍需要具有桌面访问能力的 Windows 环境。

### 克隆仓库

```bash
git clone https://github.com/wrnas/OpenTangYuan.git
cd OpenTangYuan
```

### 恢复并编译

```bash
dotnet restore TangYuan.sln
dotnet build TangYuan.sln
```

### 启动运行时

项目代码位于 `src/OpenTangYuan/`。在仓库根目录运行：

```bash
dotnet run --project src/OpenTangYuan --urls "http://localhost:54124"
```

### 验证服务

```bash
curl -X POST http://localhost:54124/api/Skills/GetSkillListForAI
```

正常情况下会返回当前可用的工作流和内置技能摘要。

### Swagger / OpenAPI

服务启动后访问：

```text
http://localhost:54124/swagger
```

![Swagger](docs/images/swagger-1.png)

配置邮箱、文件访问范围和本地程序白名单前，请先阅读 [配置与安全控制](docs/configuration-security_zh-CN.md)。

---

## 核心概念

### 技能（Skill）

每个可执行操作都表示为一个技能，例如 `email_task`、`file_task`、`browser_task` 或 `screenshot_task`。技能通过统一接口暴露给不同的 Agent 和客户端。

### 先发现，再执行

Agent 不需要预先记住所有技能及参数。推荐调用顺序为：

```text
GetSkillListForAI
        ↓
GetBuiltinSkillDetail / GetSkillAction
        ↓
ExecuteSkill / ExecuteSkillForCoze
```

### 工作流（Workflow）

多个技能可以组合为一个工作流。系统支持：

- 数据库中保存的可复用工作流；
- 请求中临时提交的多步骤工作流；
- 直接执行单个内置技能。

### 上下文变量

每一步执行结果会保存为 `step0`、`step1`、`step2` 等上下文对象。后续步骤可以引用：

```text
{{step0}}
{{step0.path}}
{{step0.data.path}}
{{step0.data.firstPath}}
{{step1.result}}
```

字段路径必须与前一步真实返回的数据结构一致。

---

## 工作流示例

以下临时工作流依次搜索文件、打开文件、截图，并将截图插入邮件正文，同时附加原文件：

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
        "body": "以下是自动截图，原文件已作为附件发送。",
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

详细的执行模式、响应结构和错误处理见 [核心 API 参考](docs/api_zh-CN.md) 与 [内置技能参考](docs/builtin-skills_zh-CN.md)。

---

## WinForms 示例客户端

仓库可在 `samples/OpenTangYuan.WinFormsDemo/` 中放置独立的 WinForms 示例客户端，用于展示普通桌面程序如何通过 REST API 调用 OpenTangYuan，而不依赖特定的 Agent 平台。

示例客户端应先连接已经启动的 OpenTangYuan 运行时，再执行能力发现、参数查询和任务调用。使用说明见 [WinForms 示例客户端](samples/README_zh-CN.md)。

---

## 内置技能

| SkillCode | 说明 |
|---|---|
| `email_task` | 搜索、读取、发送、回复邮件，下载附件、标记已读和保存 `.eml`。 |
| `wechat_task` | 向企业微信发送文本、Markdown 或卡片消息。 |
| `browser_task` | 打开网页、执行浏览器动作、提取内容、截图和下载文件。 |
| `file_task` | 搜索、复制、移动、重命名文件，创建目录和批量操作。 |
| `open_task` | 打开本地文件、目录或程序。 |
| `print_task` | 打印本地文件。 |
| `tool_task` | 调用白名单中的本地工具或可执行程序。 |
| `screenshot_task` | 截取全屏或活动窗口。 |
| `folder_task` | 按扩展名等规则整理文件。 |
| `lock_task` | 锁定本地工作站。 |

---

## 文档

- [文档导航](docs/README_zh-CN.md)
- [架构与运行机制](docs/architecture_zh-CN.md)
- [核心 API 参考](docs/api_zh-CN.md)
- [内置技能参考](docs/builtin-skills_zh-CN.md)
- [配置与安全控制](docs/configuration-security_zh-CN.md)
- [Agent 集成指南](docs/agent-integration_zh-CN.md)
- [Coze 系统提示词](docs/coze-system-prompt_zh-CN.md)
- [部署与平台支持](docs/deployment-platform_zh-CN.md)
- [故障排查](docs/troubleshooting_zh-CN.md)
- [WinForms 示例客户端](samples/README_zh-CN.md)

---

## 安全与平台边界

OpenTangYuan 能够发送邮件、修改文件、控制浏览器、截图、打印和启动本地程序，因此应运行在受信任的环境中。

部署时至少应遵循以下原则：

- 不要将运行时直接暴露到公网；
- 不要将邮箱授权码、Webhook Key、API Token 或内部系统凭据提交到仓库；
- 使用路径白名单限制文件访问范围；
- 使用程序白名单限制可以启动的可执行文件；
- 对发送邮件、删除或移动文件、打印等副作用操作保留日志；
- 对高风险操作增加人工确认或审批；
- 对外部 Agent 生成的参数进行验证，不信任未经检查的路径和命令；
- 同一副作用操作成功后不要重复执行。

完整说明见 [配置与安全控制](docs/configuration-security_zh-CN.md)。

---

## 技术栈

- .NET 8 / C#；
- ASP.NET Web API；
- SQLite / Dapper；
- MailKit；
- Playwright；
- Everything SDK 或 Windows Search；
- REST API / JSON Manifest；
- 可选 Docker 部署。

---

## 参与贡献

欢迎提交 Issue 和 Pull Request。建议在提交前：

1. 确认代码能够通过 `dotnet build TangYuan.sln`；
2. 不提交真实凭据、内部地址、私人邮件、运行日志或敏感截图；
3. 对新增技能补充清单、参数说明和调用示例；
4. 对具有副作用的操作补充边界检查和错误处理；
5. 同步更新相关文档。

---

## 许可证

本项目采用 MIT License。详情见 [LICENSE](LICENSE)。

---

## 致谢

感谢所有参与设计、开发、测试和反馈的贡献者。
