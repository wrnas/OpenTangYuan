[返回文档导航](README_zh-CN.md) · [返回项目首页](../README_zh-CN.md)

# 内置技能参考

内置技能通过统一的 `ExecuteSkill` 接口执行：

```http
POST /api/Skills/ExecuteSkill
```

请求的基本结构为：

```json
{
  "SkillCode": "skill_code",
  "Arguments": {}
}
```

Agent 应先通过 `GetSkillListForAI` 和 `GetBuiltinSkillDetail` 获取当前版本的真实能力定义，再生成参数。本文档提供常见调用方式，实际字段以技能清单和 Swagger 为准。

## 1. `email_task`

### 支持动作

| action | 说明 |
|---|---|
| `send` | 发送邮件。 |
| `search` | 搜索邮件。 |
| `read` | 读取指定搜索结果的正文。 |
| `download_attachments` | 下载指定邮件的附件。 |
| `reply` | 回复邮件。 |
| `mark_read` | 标记邮件已读。 |
| `save_eml` | 将邮件保存为 `.eml` 文件。 |

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

搜索成功后，应先向用户展示结果列表并停止。只有用户明确指定某一项时，才继续读取、下载附件、回复或标记已读。

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
    "body": "这是一封自动发送的邮件。",
    "attachments": [
      "D:\\Files\\report.docx"
    ],
    "insertImagePaths": [
      "D:\\Images\\screen.png"
    ]
  }
}
```

- `attachments`：普通附件；
- `insertImagePaths`：插入邮件正文中的图片；
- 发送成功后不要重复调用。

## 2. `file_task`

### 支持动作

| action | 说明 |
|---|---|
| `search` | 搜索文件。 |
| `copy` | 复制文件。 |
| `move` | 移动文件。 |
| `copy_many` | 批量复制。 |
| `move_many` | 批量移动。 |
| `rename` | 重命名。 |
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

文件路径必须位于允许访问的范围内。复制、移动、重命名和删除等操作成功后不要重复执行。

## 3. `browser_task`

用于执行浏览器动作序列，例如：

- 打开网页；
- 等待元素；
- 点击和输入；
- 提取文本或列表；
- 截图；
- 下载文件；
- 保持或关闭 Session。

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

网页截图使用 `browser_task`；本地桌面或本地应用窗口截图通常使用 `open_task` 与 `screenshot_task` 组合。

## 4. `wechat_task`

支持向企业微信发送：

| action | 说明 |
|---|---|
| `text` | 文本消息。 |
| `markdown` | Markdown 消息。 |
| `card` | 图文卡片。 |

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

发送成功后不要重复调用。

## 5. `open_task`

打开本地文件、目录或程序：

```json
{
  "SkillCode": "open_task",
  "Arguments": {
    "path": "D:\\Files\\report.docx"
  }
}
```

目标路径必须存在，并且运行账户需要具有访问权限。对应文件类型还需要安装默认应用程序。

## 6. `print_task`

打印本地文件：

```json
{
  "SkillCode": "print_task",
  "Arguments": {
    "path": "D:\\Files\\report.docx"
  }
}
```

打印属于副作用操作，建议在生产环境中增加确认和日志。

## 7. `screenshot_task`

用于截取本地桌面或活动窗口。常见示例：

```json
{
  "SkillCode": "screenshot_task",
  "Arguments": {
    "action": "capture_full_screen"
  }
}
```

截图产生的文件路径可以通过工作流上下文传递给邮件或其他步骤。具体返回字段以当前技能详情为准。

## 8. `tool_task`

调用允许的本地工具或可执行程序：

```json
{
  "SkillCode": "tool_task",
  "Arguments": {
    "exePath": "D:\\Tools\\LmyTools.exe",
    "arguments": "--help",
    "timeout": 10
  }
}
```

只能调用程序白名单允许的可执行文件。不要将模型生成的任意命令直接交给系统执行。

## 9. 其他技能

| SkillCode | 说明 |
|---|---|
| `folder_task` | 按扩展名等规则整理文件。 |
| `lock_task` | 锁定本地工作站。 |

如果当前部署增加了其他技能，应通过能力发现接口读取其定义，不应假设所有安装实例拥有相同的扩展技能。

## 10. 调用安全规则

- 缺少路径、收件人、文件名或其他必要参数时先询问用户；
- 不猜测本地路径或中间结果；
- 列表查询成功后先展示结果并停止；
- 副作用操作成功后立即停止；
- 同一技能失败后最多修正参数重试一次；
- 文件和程序操作必须受白名单限制；
- 实际调用前应查看当前技能详情。
