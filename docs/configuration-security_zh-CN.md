[返回文档导航](README_zh-CN.md) · [返回项目首页](../README_zh-CN.md)

# 配置与安全控制

OpenTangYuan 可以访问邮箱、文件系统、浏览器、桌面程序和企业系统。配置时应遵循最小权限原则，并将开发环境与生产环境分开。

## 1. 敏感信息管理

不要将以下内容提交到 Git 仓库：

- 邮箱密码或授权码；
- API Key；
- 企业消息 Webhook Key；
- 数据库密码；
- 内部系统 Token；
- 真实内网地址；
- 私人邮件、文件路径、截图和运行日志；
- 包含敏感数据的 SQLite 数据库。

推荐使用：

- 环境变量；
- .NET User Secrets；
- Docker Secrets；
- CI/CD 密钥变量；
- 单独的生产配置文件；
- 操作系统或企业级密钥管理服务。

示例配置中只应保留占位值。

## 2. 邮箱配置

示例：

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

配置前请确认邮箱服务商已经启用 SMTP、IMAP 和第三方客户端访问。许多服务商要求使用授权码，而不是账户登录密码。

## 3. 文件访问白名单

示例：

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

建议：

- 只允许业务必需的目录；
- 不要直接允许整个系统盘；
- 对网络共享目录单独评估权限；
- 写操作与只读操作可以采用不同策略；
- 对用户输入和 Agent 参数进行路径规范化，防止越界访问。

## 4. 可执行程序白名单

示例：

```json
{
  "AllowedExeNames": [
    "pandoc.exe",
    "custom-tool.exe"
  ]
}
```

建议：

- 仅允许明确需要的程序；
- 不要允许通用命令解释器或任意脚本执行器，除非部署环境有额外隔离；
- 记录程序路径、参数、执行时间和结果；
- 对超时、退出码和输出大小进行限制。

## 5. 常见配置项

| 配置项 | 必需性 | 说明 |
|---|---|---|
| `EmailSettings:SmtpServer` | 按需 | SMTP 服务器。 |
| `EmailSettings:SmtpPort` | 按需 | SMTP 端口。 |
| `EmailSettings:SenderEmail` | 按需 | 发件邮箱。 |
| `EmailSettings:SenderPassword` | 按需 | 邮箱授权码。 |
| `EmailSettings:ImapServer` | 按需 | IMAP 服务器。 |
| `ConnectionStrings:Sqlite` | 是 | 工作流等数据的 SQLite 连接字符串。 |
| `FileSystem:AllowedRoots` | 强烈建议 | 运行时可访问的目录范围。 |
| `AllowedExeNames` | 强烈建议 | 允许启动的程序。 |
| `DebugMode` | 可选 | 是否返回或记录更详细的调试信息。 |

实际配置名称应以当前版本的 `appsettings.json` 和代码为准。

## 6. API 访问控制

开发环境和生产环境的安全要求不同。

生产部署应至少选择一种或多种保护方式：

- HTTPS；
- API Key；
- 反向代理认证；
- VPN；
- IP 白名单；
- 内网网关；
- 用户或服务身份认证。

不要假设本地开发配置已经自动启用所有生产级认证。部署人员应检查控制器授权、网关规则和 Swagger 访问策略。

## 7. Swagger

在开发环境中，Swagger 便于调试接口。生产环境中建议：

- 关闭 Swagger UI；或
- 对 Swagger 增加身份认证；或
- 仅允许管理网络访问。

## 8. 副作用操作

以下操作需要特别控制：

- 发送或回复邮件；
- 下载附件；
- 复制、移动、重命名或删除文件；
- 打印；
- 启动程序；
- 向企业平台发送消息；
- 调用内部系统的写操作。

建议采用：

1. 参数校验；
2. 路径和程序白名单；
3. 请求幂等或去重机制；
4. 成功后停止规则；
5. 高风险操作人工确认；
6. 执行日志和审计记录；
7. 限制失败重试次数。

## 9. 日志与隐私

日志应足以支持排查问题，但不应无条件记录敏感内容。

建议记录：

- 请求时间；
- 调用的 `SkillCode`；
- 执行模式；
- 成功或失败状态；
- 错误码；
- 耗时；
- 经脱敏的目标信息。

避免记录：

- 邮箱密码和授权码；
- 完整邮件正文；
- 完整 API Token；
- 未脱敏的内部文件路径；
- 用户私人数据；
- 不必要的截图内容。

## 10. 生产部署检查清单

- [ ] 已启用 HTTPS 或安全网关；
- [ ] 运行时未直接暴露到公网；
- [ ] 已配置认证和访问来源限制；
- [ ] Swagger 已关闭或受保护；
- [ ] 邮箱和 Webhook 凭据未写入仓库；
- [ ] 文件路径白名单已收紧；
- [ ] 程序白名单已收紧；
- [ ] 高风险副作用操作具有确认或审批；
- [ ] 关键操作具有日志；
- [ ] 日志和数据库中没有不必要的敏感数据；
- [ ] 已验证备份、恢复和凭据轮换方式。

## 11. 相关文档

- [部署与平台支持](deployment-platform_zh-CN.md)
- [Agent 集成指南](agent-integration_zh-CN.md)
- [故障排查](troubleshooting_zh-CN.md)
