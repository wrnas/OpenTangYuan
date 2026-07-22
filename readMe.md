# OpenTangYuan

<p align="center">
  <strong>云端规划，本地执行</strong><br>
  <strong>Cloud Planning, Local Execution</strong>
</p>

<p align="center">
  面向隐私敏感办公自动化的智能体工作流运行时<br>
  An agent workflow runtime for privacy-sensitive office automation
</p>

<p align="center">
  <a href="README.zh-CN.md"><strong>简体中文</strong></a>
  &nbsp;&nbsp;·&nbsp;&nbsp;
  <a href="README.en.md"><strong>English</strong></a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4" alt=".NET 8">
  <img src="https://img.shields.io/badge/License-MIT-2EA44F" alt="MIT License">
  <img src="https://img.shields.io/badge/Platform-Windows--first-0078D4" alt="Windows-first">
  <img src="https://img.shields.io/badge/API-REST%20%2F%20OpenAPI-0052CC" alt="REST / OpenAPI">
</p>

---

## 简介 / Overview

**OpenTangYuan** 是一个开源的智能体工作流运行时。外部 AI Agent 负责理解用户意图、发现能力和规划任务，可信本地 Runtime 负责访问文件、邮件、浏览器、本地工具与企业系统，并在用户环境中完成真实执行。

**OpenTangYuan** is an open-source agent workflow runtime. External AI agents handle intent understanding, capability discovery, and task planning, while a trusted local Runtime performs real operations on files, email, browsers, local tools, and enterprise systems.

```text
AI Agent
理解意图 · 发现能力 · 规划任务
Intent · Discovery · Planning
            ↓
OpenTangYuan Runtime
工作流执行 · 上下文管理 · 策略校验
Execution · Context · Policy
            ↓
Local & Enterprise Resources
文件 · 邮件 · 浏览器 · 工具 · 内部系统
Files · Email · Browser · Tools · Internal Systems
```

## 核心能力 / Key Capabilities

| 能力 | Capability | 说明 |
|---|---|---|
| 工作流执行 | Workflow Execution | 支持数据库工作流、临时工作流和单步技能 |
| 能力发现 | Capability Discovery | Agent 按需查询技能、参数和工作流定义 |
| 上下文传递 | Context Propagation | 后续步骤可引用前序步骤的结构化结果 |
| 本地执行 | Trusted Local Execution | 敏感数据和执行权限保留在用户环境中 |
| 外部接入 | Agent Integration | 支持 Coze、Dify、GPTs 和自定义客户端 |
| 安全控制 | Security Controls | 支持白名单、策略校验、日志和部署隔离 |

## 架构 / Architecture

<p align="center">
  <img src="docs/images/architecture.png" alt="OpenTangYuan architecture" width="900">
</p>

<p align="center">
  <a href="docs/architecture_zh-CN.md">中文架构说明</a>
  &nbsp;·&nbsp;
  <a href="docs/architecture.md">Architecture Guide</a>
</p>

## 快速开始 / Quick Start

### 获取代码 / Clone

```bash
# Gitee（主仓库 / Primary）
git clone https://gitee.com/l00f/open-tang-yuan.git

# GitHub（同步镜像 / Mirror）
git clone https://github.com/wrnas/OpenTangYuan.git
```

### 构建与运行 / Build and Run

```bash
dotnet restore TangYuan.sln
dotnet run --project src/OpenTangYuan/TangYuan.csproj --urls "http://localhost:54124"
```

启动后访问 / Open after startup:

```text
http://localhost:54124/swagger
```

> 完整桌面自动化能力以 Windows 为主。Linux 和 Docker 适合运行 Web API、Swagger、能力发现以及不依赖桌面资源的功能。  
> Full desktop automation is Windows-first. Linux and Docker are suitable for the Web API, Swagger, capability discovery, and features that do not require desktop resources.

## 文档 / Documentation

<table>
  <tr>
    <td valign="top" width="50%">
      <strong>中文文档</strong><br><br>
      <a href="README.zh-CN.md">完整中文说明</a><br>
      <a href="docs/README_zh-CN.md">中文文档中心</a><br>
      <a href="docs/api_zh-CN.md">API 参考</a><br>
      <a href="docs/builtin-skills_zh-CN.md">内置技能</a><br>
      <a href="docs/agent-integration_zh-CN.md">Agent 接入</a><br>
      <a href="docs/configuration-security_zh-CN.md">配置与安全</a><br>
      <a href="docs/deployment-platform_zh-CN.md">部署与平台</a><br>
      <a href="docs/troubleshooting_zh-CN.md">故障排查</a>
    </td>
    <td valign="top" width="50%">
      <strong>English Documentation</strong><br><br>
      <a href="README.en.md">Full English README</a><br>
      <a href="docs/README.md">Documentation Home</a><br>
      <a href="docs/api.md">API Reference</a><br>
      <a href="docs/builtin-skills.md">Built-in Skills</a><br>
      <a href="docs/agent-integration.md">Agent Integration</a><br>
      <a href="docs/configuration-security.md">Configuration and Security</a><br>
      <a href="docs/deployment-platform.md">Deployment and Platforms</a><br>
      <a href="docs/troubleshooting.md">Troubleshooting</a>
    </td>
  </tr>
</table>

## Demo 与发布 / Demo and Releases

- [GitHub Releases](https://github.com/wrnas/OpenTangYuan/releases)
- [WinForms Demo](samples/OpenTangYuan.WinFormsDemo/)
- [Demo 中文说明](samples/OpenTangYuan.WinFormsDemo/README_zh-CN.md)
- [Demo English Guide](samples/OpenTangYuan.WinFormsDemo/README.md)

## 仓库 / Repositories

- **Gitee — 主仓库 / Primary:** https://gitee.com/l00f/open-tang-yuan
- **GitHub — 同步镜像 / Mirror:** https://github.com/wrnas/OpenTangYuan

## 许可证 / License

OpenTangYuan is released under the [MIT License](LICENSE).