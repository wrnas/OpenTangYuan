# OpenTangYuan

<p align="center">
  <strong>云端规划、本地执行的智能体工作流运行时</strong><br>
  <strong>Cloud-Planned, Locally Executed Agent Workflow Runtime</strong>
</p>

<p align="center">
  面向隐私敏感的办公自动化与机构工作流<br>
  For privacy-sensitive office automation and institutional workflows
</p>

<p align="center">
  <a href="README_zh-CN.md"><strong>简体中文</strong></a>
  &nbsp;·&nbsp;
  <a href="README_EN.md"><strong>English</strong></a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4" alt=".NET 8">
  <img src="https://img.shields.io/badge/license-MIT-green" alt="MIT License">
  <img src="https://img.shields.io/badge/platform-Windows--first-0078D4" alt="Windows-first">
  <img src="https://img.shields.io/badge/API-REST%20%2F%20OpenAPI-blue" alt="REST API">
</p>

---

## 项目简介 / Overview

**OpenTangYuan** 是一个开源的智能体工作流运行时。外部 AI Agent 负责理解用户意图、发现能力和规划任务，本地 Runtime 负责访问文件、邮件、浏览器、本地工具及企业系统，并在受信任环境中完成真实执行。

**OpenTangYuan** is an open-source agent workflow runtime. External AI agents handle intent understanding, capability discovery, and task planning, while the trusted local Runtime performs real operations on files, email, browsers, local tools, and enterprise systems.

```text
Cloud / 云端
Planning · Capability Discovery · Parameter Generation
规划 · 能力发现 · 参数生成
                    ↓
Trusted Local Runtime / 可信本地运行时
Workflow Execution · Policy Checks · Context Management
工作流执行 · 策略校验 · 上下文管理
                    ↓
Local & Enterprise Resources / 本地与企业资源
Files · Email · Browser · Tools · Internal Systems
文件 · 邮件 · 浏览器 · 本地工具 · 内部系统
```

## 核心能力 / Key Capabilities

| 能力 | Capability | 说明 |
|---|---|---|
| 工作流执行 | Workflow execution | 支持预定义工作流、临时工作流和单步技能 |
| 能力发现 | Capability discovery | Agent 可按需查询技能、参数和工作流定义 |
| 上下文传递 | Context propagation | 后续步骤可引用前序步骤的结构化结果 |
| 本地执行 | Trusted local execution | 敏感数据和执行权限保留在用户环境中 |
| 外部集成 | External integration | 可接入 Coze、Dify、GPTs 和自定义客户端 |
| 安全控制 | Security controls | 支持路径白名单、程序白名单、日志和部署策略 |

## 架构 / Architecture

<p align="center">
  <img src="docs/images/architecture.png" alt="OpenTangYuan architecture" width="900">
</p>

详细说明 / Learn more:

- [中文架构说明](docs/architecture_zh-CN.md)
- [Architecture Guide](docs/architecture.md)

## 快速开始 / Quick Start

### 从 Gitee 获取 / Clone from Gitee

```bash
git clone https://gitee.com/l00f/open-tang-yuan.git
cd open-tang-yuan
```

### 从 GitHub 获取 / Clone from GitHub

```bash
git clone https://github.com/wrnas/OpenTangYuan.git
cd OpenTangYuan
```

### 构建并运行 / Build and Run

```bash
dotnet restore TangYuan.sln
dotnet run --project src/OpenTangYuan/TangYuan.csproj --urls "http://localhost:54124"
```

运行后访问 / After startup, open:

```text
http://localhost:54124/swagger
```

> 完整桌面自动化能力以 Windows 为主；Linux 和 Docker 适合运行 Web API、Swagger、能力发现及不依赖桌面资源的功能。  
> Full desktop automation is Windows-first. Linux and Docker are suitable for the Web API, Swagger, capability discovery, and features that do not require desktop resources.

## 文档 / Documentation

### 中文

- [完整中文说明](README_zh-CN.md)
- [中文文档中心](docs/README_zh-CN.md)
- [API 参考](docs/api_zh-CN.md)
- [内置技能](docs/builtin-skills_zh-CN.md)
- [Agent 接入](docs/agent-integration_zh-CN.md)
- [配置与安全](docs/configuration-security_zh-CN.md)
- [部署与平台](docs/deployment-platform_zh-CN.md)
- [故障排查](docs/troubleshooting_zh-CN.md)

### English

- [Full English README](README_EN.md)
- [Documentation Home](docs/README.md)
- [API Reference](docs/api.md)
- [Built-in Skills](docs/builtin-skills.md)
- [Agent Integration](docs/agent-integration.md)
- [Configuration and Security](docs/configuration-security.md)
- [Deployment and Platforms](docs/deployment-platform.md)
- [Troubleshooting](docs/troubleshooting.md)

## Demo 与发布 / Demo and Releases

- [GitHub Releases](https://github.com/wrnas/OpenTangYuan/releases)
- [WinForms Demo](samples/OpenTangYuan.WinFormsDemo/)
- [Demo 中文说明](samples/OpenTangYuan.WinFormsDemo/README_zh-CN.md)
- [Demo English Guide](samples/OpenTangYuan.WinFormsDemo/README.md)

## 仓库 / Repositories

- **Gitee（主仓库 / Primary）**: https://gitee.com/l00f/open-tang-yuan
- **GitHub（同步镜像 / Mirror）**: https://github.com/wrnas/OpenTangYuan

## 许可证 / License

OpenTangYuan is released under the [MIT License](LICENSE).