[English](builtin-skills.md) · [返回文档导航](README_zh-CN.md) · [返回项目首页](../README_zh-CN.md)

# 内置技能参考

OpenTangYuan 将本地能力封装为内置技能。技能通过 `ExecuteSkill` 执行，其当前支持的动作、参数、示例、约束和副作用可以通过 `GetBuiltinSkillDetail` 查询。

以下示例说明常见调用方式。当前版本的 `skill-manifest.json`、Swagger 定义和实际接口响应应作为最终依据。

## 1. 通用请求格式

```json
{
  "SkillCode": "file_task",
  "Arguments": {
    "action": "search"
  }
}
```

临时多步骤工作流将内置技能放入 `Steps`：

```json
{
  "SkillCode": "temp_task",
  "Arguments": {},
  "Steps": [
    {
      "Action": "file_task",
      "Args": {
        "action": "search",
        "keyword": "报告",
        "ext": "docx"
      }
    }
  ]
}
```

## 2. `email_task`

常见邮箱动作包括：

| action | 用途 |
|---|---|
| `send` | 发送新邮件。 |
| `search` | 搜索邮件并返回列表。 |
| `read` | 读取指定邮件内容。 |
| `download_attachments` | 下载指定邮件的附件。 |
| `reply` | 回复指定邮件。 |
| `mark_read` | 将指定邮件标记为已读。 |
| `save_eml` | 将指定邮件保存为 `.eml` 文件。 |

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

返回列表后，应先向用户展示结果并停止。只有用户明确指定邮件后，才继续读取或执行副作用操作。

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
    "body": "这是一封由 OpenTangYuan 发送的邮件。",
    "attachments": [
      "D:\\Files\\report.docx"
    ],
    "insertImagePaths": [
      "D:\\Images\\screen.png"
    ]
  }
}
```

普通附件使用 `attachments`，嵌入邮件正文的图片使用 `insertImagePaths`。发送或回复成功后应立即停止。

## 3. `file_task`

常见文件动作包括：

| action | 用途 |
|---|---|
| `search` | 搜索文件。 |
| `copy` | 复制单个文件。 |
| `move` | 移动单个文件。 |
| `copy_many` | 批量复制文件。 |
| `move_many` | 批量移动文件。 |
| `rename` | 重命名文件或目录。 |
| `mkdir` | 创建目录。 |

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

所有文件路径都应经过校验，并限制在配置的允许范围内。会修改文件状态的操作成功后不得自动重复执行。

## 4. `browser_task`

`browser_task` 用于执行浏览器自动化。当前清单可能支持：

- 打开网页；
- 等待元素；
- 点击；
- 输入文本；
- 提取文本或列表；
- 截取网页；
- 下载文件；
- 保持或关闭浏览器 Session。

示例：

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

网页截图使用 `browser_task`；本地桌面或本地应用窗口截图使用 `screenshot_task`。

## 5. `wechat_task`

常见企业微信动作包括：

| action | 用途 |
|---|---|
| `text` | 发送文本消息。 |
| `markdown` | 发送 Markdown 消息。 |
| `card` | 发送卡片消息。 |

示例：

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

消息发送成功属于副作用操作，不应自动重复调用。

## 6. `open_task`

打开本地文件、目录或应用程序：

```json
{
  "SkillCode": "open_task",
  "Arguments": {
    "path": "D:\\Files\\report.docx"
  }
}
```

目标应真实存在、位于允许范围内，并在需要时安装了关联应用程序。

## 7. `print_task`

打印本地文件：

```json
{
  "SkillCode": "print_task",
  "Arguments": {
    "path": "D:\\Files\\report.docx"
  }
}
```

打印属于副作用操作。执行前应确认文件、打印机状态和用户意图。

## 8. `screenshot_task`

截取本地桌面或活动应用窗口：

```json
{
  "SkillCode": "screenshot_task",
  "Arguments": {
    "action": "capture_full_screen"
  }
}
```

具体动作和返回字段应通过 `GetBuiltinSkillDetail` 查询。桌面截图要求运行在具有桌面访问能力的 Windows 会话中。

## 9. `tool_task`

调用白名单中的本地工具或可执行程序：

```json
{
  "SkillCode": "tool_task",
  "Arguments": {
    "exePath": "D:\\Tools\\CustomTool.exe",
    "arguments": "--help",
    "timeout": 10
  }
}
```

仅应允许明确批准的程序。应校验程序路径和参数，限制超时时间，并记录执行结果。

## 10. `folder_task`

`folder_task` 用于按照扩展名等规则整理文件。当前版本支持的动作名称和参数应通过 `GetBuiltinSkillDetail` 获取。

## 11. `lock_task`

`lock_task` 用于锁定本地工作站。该操作会改变用户桌面状态，只有在用户意图明确时才应执行。

## 12. 技能发现与版本差异

不同部署可能暴露不同的技能集合或参数结构。Agent 和客户端应：

1. 调用 `GetSkillListForAI` 获取当前能力目录；
2. 在构造参数前调用 `GetBuiltinSkillDetail`；
3. 只使用当前运行时返回的动作和字段；
4. 对缺失的可选技能进行合理降级；
5. 不在不兼容版本之间长期缓存技能定义。

## 13. 相关文档

- [核心 API 参考](api_zh-CN.md)
- [架构与运行机制](architecture_zh-CN.md)
- [Agent 集成指南](agent-integration_zh-CN.md)
- [配置与安全控制](configuration-security_zh-CN.md)
