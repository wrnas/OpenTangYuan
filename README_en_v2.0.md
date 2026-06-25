# OpenTangYuan  Cloud-Local Agent Workflow Runtime for Privacy-Sensitive Office Automation 

---

## 🚀 Typical Workflows (What it can do)

**让枯燥的文字动起来**：OpenTangYuan 擅长处理跨系统的复杂任务。以下是它能为你做的典型工作：

- 🔍 **文件处理流**：`Search file` → `Open` → `Screenshot` → `Email`
- 📧 **邮件自动化**：`Read inbox` → `Download attachments` → `Organize` → `Notify`
- 🏢 **内部系统集成**：`Open enterprise system` → `Export` → `Capture` → `Send message`

---

## 📖 What is OpenTangYuan?

**OpenTangYuan** 是一个开源的 **Agent 工作流运行时（Agent Workflow Runtime）**，专为对隐私敏感的办公自动化（RPA）和机构流程设计。

在很多场景下，云端的 AI Agent 能理解你的需求并规划任务，但实际执行需要访问本地的文件、邮件、浏览器或内部系统。这些资源绝不应该直接暴露给云端。

OpenTangYuan 的核心理念是 **“云-端分离”** ：

```text
Cloud side: planning and capability metadata
Local runtime: execution and sensitive data processing 
Enterprise systems: accessed only through the trusted local runtime
```

**一句话总结**：让 AI 负责思考和规划，让本地运行时（Runtime）负责执行和保护敏感数据。

---

## 💡 Core Concepts

如果你是第一次接触，只需要记住这三个词：**技能(Skills)**、**工作流(Workflows)** 和 **可信本地运行时**。

### 1. Skills: Everything Is a Capability

一切皆能力。OpenTangYuan 将所有操作（文件、邮件、浏览器、截图）都封装成可被发现的"技能"（Skill）。

- **File Task**: 搜索、复制、移动、重命名。
- **Email Task**: 收发邮件、下载附件。
- **Browser Task**: 浏览网页、截图、提取内容。
- **WeChat/Work Task**: 推送消息到企业微信。

### 2. Discover Before You Execute (先发现，后执行)

AI 不需要提前记住所有工具的参数。它通过简单的三步曲来调用能力：

1. `GetSkillListForAI` (获取能力列表)
2. `GetBuiltinSkillDetail` (按需获取详情)
3. `ExecuteSkill` (执行)

### 3. Workflows: 建立复杂的任务流

真实世界的工作往往不止一步。OpenTangYuan 支持将多个技能组合成工作流。
例如：`搜索文件` -> `打开文件` -> `截图` -> `发送邮件`。

### 4. Context Passing: 步骤间的数据传递

后一步可以自动引用前一步的结果。使用 `` 这样的模板变量，让数据在步骤间无缝流转。

### 5. Local Execution: 安全边界

所有涉及敏感数据的操作（读取私密文件、发送邮件、截取屏幕）**永远在本地运行时执行**。云端只负责传递指令和接收结果。

### 6. Safety Layer: 内置安全锁

为了防止误操作，运行时内置了安全机制：

- 🔒 **API 认证**与**路径白名单**。
- 📜 **执行日志**与**策略验证**。
- 🛡️ **可执行文件白名单**。

---

## 🏗️ System Architecture

OpenTangYuan 遵循云-端协作模型。

| Layer | Responsibility |
| ------ |------ |
| **User Interaction** | Web, Chat, Coze, Dify, GPTs |
| **Orchestration** | Workflow repository, Skill registry |
| **Secure Channel** | Communication between cloud and local |
| **Trusted Local Runtime** | **Authentication, Policy, Execution** |
| **Enterprise Integration** | File, Email, Browser, OA, ERP |
| **Governance** | Privacy, Auditing, Monitoring |

---

## 🛠️ Quick Start

### Requirements

- .NET 8 SDK or Runtime
- Windows 10/11 (推荐，支持完整桌面自动化) 或 Linux/Docker (服务端组件)
- SQLite

### Run it

```bash
git clone https://github.com/wrnas/OpenTangYuan.git
cd OpenTangYuan
dotnet restore
dotnet run --urls "http://localhost:54124"
```

### Verify

```bash
curl -X POST http://localhost:54124/api/Skills/GetSkillListForAI
```

访问 `http://localhost:54124/swagger` 查看交互式 API 文档。

---

## 📚 Research Context

OpenTangYuan 不仅是一个开源工具，也是我们发表在 SoftwareX 的研究成果。它解决了传统 AI Agent 在处理办公自动化时面临的隐私泄露和复杂任务编排难题。

### Why it matters?

1. **清单驱动 (Manifest-driven)**：技能描述结构化，AI 按需发现，而非硬编码。
2. **工作流优先 (Workflow-first)**：支持预定义的稳定工作流，也支持临时的动态组合。
3. **可信本地运行时**：将"能力"与"数据"解耦，确保敏感信息不出内网。

---

## 📝 Citation

**如果你在研究中使用了 OpenTangYuan，请引用我们的工作：**

@software{opentangyuan,
title = {OpenTangYuan: A cloud-local agent workflow runtime for privacy-sensitive office automation},
author = {Liu, Mingyang and Contributors},
year = {2026},
url = {https://github.com/wrnas/OpenTangYuan},
license = {MIT},
version = {v1.1.2}
}

---

## 📄 License

This project is licensed under the MIT License.

