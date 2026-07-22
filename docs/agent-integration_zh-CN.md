[返回文档导航](README_zh-CN.md) · [返回项目首页](../README_zh-CN.md)

# Agent 集成指南

OpenTangYuan 可以作为 Coze、Dify、GPTs、自定义 Agent 或普通客户端的本地执行运行时。

外部 Agent 负责：

- 理解用户需求；
- 查询本地可用能力；
- 选择工作流或内置技能；
- 补齐或询问必要参数；
- 构造单步或多步请求。

OpenTangYuan 负责：

- 验证和执行请求；
- 访问本地与企业资源；
- 解析工作流模板变量；
- 管理步骤上下文；
- 返回结构化结果。

## 1. 推荐调用流程

```text
用户请求
   ↓
GetSkillListForAI
   ↓
是否存在匹配工作流？
   ├─ 是 → GetSkillAction
   └─ 否 → GetBuiltinSkillDetail
   ↓
检查必要参数
   ↓
ExecuteSkill / ExecuteSkillForCoze
   ↓
展示结果并停止
```

## 2. 推荐插件或工具定义

Agent 平台通常只需要配置以下核心能力：

| 工具 | 路径 | 用途 |
|---|---|---|
| `GetSkillListForAI` | `/api/Skills/GetSkillListForAI` | 获取工作流和内置技能摘要。 |
| `GetSkillAction` | `/api/Skills/GetSkillAction` | 获取已保存工作流的步骤。 |
| `GetBuiltinSkillDetail` | `/api/Skills/GetBuiltinSkillDetail` | 获取内置技能的参数和动作。 |
| `ExecuteSkill` | `/api/Skills/ExecuteSkill` | 执行内置技能、数据库工作流或临时工作流。 |
| `ExecuteSkillForCoze` | `/api/Skills/ExecuteSkillForCoze` | 使用字符串参数执行，适合受限平台。 |

Agent 不需要在系统提示词中长期保存所有内置技能参数。应通过能力发现接口读取当前部署的真实定义。

## 3. 决策规则

1. 每次接到新的任务时，先查询能力目录；
2. 如果存在匹配的工作流，先读取工作流详情；
3. 如果没有匹配工作流，再读取相关内置技能详情；
4. 只使用接口返回的能力名称和参数定义；
5. 缺少必需参数时询问用户；
6. 不猜测邮箱、收件人、文件名、本地路径或凭据；
7. 执行完成后，根据返回结果决定是否停止。

## 4. 停止规则

### 普通成功

当任务目标已经完成时，立即停止，不要继续调用其他技能。

### 列表查询

邮件搜索、文件搜索等接口返回列表后：

1. 向用户展示列表；
2. 停止执行；
3. 只有用户明确要求查看某一项、下载附件或继续操作时，才发起下一次调用。

### 副作用操作

以下操作成功后必须停止：

- 发送或回复邮件；
- 复制、移动、重命名或删除文件；
- 下载附件；
- 标记邮件已读；
- 保存文件；
- 打印；
- 启动本地程序；
- 发送企业消息。

不要因为没有收到自然语言确认而重复执行已经成功的操作。

### 失败重试

同一技能失败后，可以在明确修正参数的情况下重试一次。第二次仍失败时，应向用户报告错误并停止。

## 5. 参数格式

`ExecuteSkill` 只使用以下顶层字段：

```text
SkillCode
Arguments
Steps
```

无参数时应传：

```json
{
  "Arguments": {}
}
```

不要增加未经接口定义的顶层字段。

### 单步示例

```json
{
  "SkillCode": "wechat_task",
  "Arguments": {
    "action": "text",
    "content": "任务已完成"
  }
}
```

### 临时工作流示例

```json
{
  "SkillCode": "temp_task",
  "Arguments": {},
  "Steps": [
    {
      "Action": "browser_task",
      "Args": {
        "actions": [
          {
            "type": "goto",
            "url": "https://example.com"
          },
          {
            "type": "get_text",
            "selector": "body"
          }
        ]
      }
    },
    {
      "Action": "wechat_task",
      "Args": {
        "action": "text",
        "content": "任务已完成"
      }
    }
  ]
}
```

## 6. 工作流上下文

后续步骤应引用运行时生成的上下文，而不是猜测中间结果：

```text
{{step0}}
{{step0.data.path}}
{{step0.data.firstPath}}
{{step1.result}}
```

Agent 应根据前一步真实响应选择字段路径。

## 7. 邮件调用规则

推荐流程：

```text
search → 展示列表并停止
用户指定项目后 → read / download_attachments / reply / mark_read / save_eml
```

发送邮件时：

- `attachments` 用于普通附件；
- `insertImagePaths` 用于嵌入正文的图片；
- 发送成功后立即停止。

## 8. 截图规则

- 网页截图：使用 `browser_task`；
- 本地桌面或本地程序窗口：使用 `open_task` 与 `screenshot_task`；
- 不要在没有必要时混合两种方式。

## 9. 部署特定工作流

某些安装环境可能包含办公系统消息检查、内部审批或其他自定义工作流。这些能力不是所有部署都默认存在。

Agent 应通过 `GetSkillListForAI` 判断当前实例是否提供相应工作流，不应硬编码部署特定的 `SkillCode`。

## 10. Coze 集成

Coze 等平台可以使用 `ExecuteSkillForCoze` 传递序列化 JSON。可参考 [Coze 系统提示词](coze-system-prompt_zh-CN.md)，并根据当前部署的接口地址、认证方式和技能目录进行调整。

## 11. 安全要求

- 外部 Agent 生成的参数必须由本地运行时校验；
- Agent 不应绕过白名单或权限策略；
- 不在提示词中写入真实凭据；
- 不将内部系统数据回传到不受信任的平台；
- 高风险任务应增加人工确认；
- 对副作用操作使用严格停止规则。
