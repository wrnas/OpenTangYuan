[返回项目首页](../README_zh-CN.md)

# OpenTangYuan 文档导航

本目录存放 OpenTangYuan 的详细说明。项目首页负责介绍软件、快速启动和核心概念；本目录中的文档负责解释架构、API、配置、部署、Agent 集成和故障排查。

## 文档列表

| 文档 | 面向读者 | 主要内容 |
|---|---|---|
| [架构与运行机制](architecture_zh-CN.md) | 架构师、开发者、集成方 | 云端与本地边界、能力发现、工作流运行时、上下文传递和扩展机制。 |
| [核心 API 参考](api_zh-CN.md) | Agent 开发者、客户端开发者 | 能力发现、工作流读取、统一执行、响应格式和调用原则。 |
| [内置技能参考](builtin-skills_zh-CN.md) | 集成开发者、技能开发者 | 邮件、文件、浏览器、消息、本地工具等内置技能的动作和示例。 |
| [配置与安全控制](configuration-security_zh-CN.md) | 部署人员、管理员 | 邮箱配置、路径白名单、程序白名单、凭据管理和生产安全建议。 |
| [Agent 集成指南](agent-integration_zh-CN.md) | Agent 平台开发者 | 标准调用链路、插件设计、停止规则、参数处理和副作用控制。 |
| [Coze 系统提示词](coze-system-prompt_zh-CN.md) | Coze 用户 | 可复制并按部署情况调整的 Agent 系统提示词。 |
| [部署与平台支持](deployment-platform_zh-CN.md) | 运维、开发者 | Windows、Linux、Docker 的适用范围，构建、运行和发布建议。 |
| [故障排查](troubleshooting_zh-CN.md) | 用户、运维、开发者 | 邮箱、文件、浏览器、工作流上下文和常见错误的排查方法。 |

## 阅读建议

- **第一次了解项目**：先阅读根目录 `README_zh-CN.md`。
- **开发客户端或 Agent 插件**：阅读 `api_zh-CN.md` 和 `agent-integration_zh-CN.md`。
- **配置真实办公环境**：阅读 `configuration-security_zh-CN.md`。
- **扩展技能或工作流**：阅读 `architecture_zh-CN.md` 和 `builtin-skills_zh-CN.md`。
- **准备部署**：阅读 `deployment-platform_zh-CN.md`。

## 文档约定

- API 路径、字段名、`SkillCode` 和模板变量保持英文原名；
- 示例中的邮箱、路径、域名和凭据均为占位内容；
- 实际端口以启动日志、`launchSettings.json` 或部署配置为准；
- 对具有副作用的接口，应先在隔离环境中测试；
- 文档示例应与当前发布版本的实际返回结构保持一致。
