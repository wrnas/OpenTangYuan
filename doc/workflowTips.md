## 工作流编排和使用示例

这一节只讲一件事：

> 如何从“最简单调用”开始，逐步看懂 workflow 的写法和用法。

---

## 1. 先理解 3 个概念

### 1）builtin 原子技能
就是单一步骤能力，例如：

- `browser_task`
- `file_task`
- `tool_task`
- `email_task`
- `wechat_task`

---

### 2）workflow 工作流
就是把多个原子技能按顺序组合起来执行，例如：

- 截图 → 发微信
- 搜索文件 → 复制 → 发邮件
- 登录系统 → 获取未读列表 → 发微信

---

### 3）统一执行入口
所有执行都走：

```text
ExecuteSkill
```

---

## 2. 最简单的调用：不传参数，直接执行一个 builtin

### 示例：执行截图技能

```json
{
  "SkillCode": "screenshot_task",
  "Arguments": {}
}
```

### 说明
这不是 workflow，只是直接调用一个原子技能。

### 适合场景
- 先验证某个 builtin 是否正常
- 调试单个技能

---

## 3. 最简单的工作流：不传参数，直接执行 2 个步骤

### 示例：截图后发企业微信

```json
{
  "SkillCode": "temp_screenshot_and_wechat",
  "Arguments": {},
  "Steps": [
    {
      "Action": "screenshot_task",
      "Args": {}
    },
    {
      "Action": "wechat_task",
      "Args": {
        "action": "markdown",
        "content": "截图完成。"
      }
    }
  ]
}
```

### 说明
这个就是最简单的 workflow：

- 第 1 步：截图
- 第 2 步：发微信

这里没有使用模板变量，也没有传参数，所以最容易理解。

---

## 4. 最简单的“带步骤引用”的工作流

这个例子开始展示 workflow 的核心价值：

> 后一步可以引用前一步的结果

### 示例：截图后，把截图路径写进微信内容

```json
{
  "SkillCode": "temp_screenshot_path_and_wechat",
  "Arguments": {},
  "Steps": [
    {
      "Action": "screenshot_task",
      "Args": {}
    },
    {
      "Action": "wechat_task",
      "Args": {
        "action": "markdown",
        "content": "截图完成，文件路径：{{step0.Data.path}}"
      }
    }
  ]
}
```

### 说明
这里用到了模板变量：

```text
{{step0.Data.path}}
```

意思是：

- `step0` = 第 0 步的执行结果
- `Data.path` = 第 0 步结果里的路径字段

---

## 5. 最简单的“保存后再调用”的示例

上面都是临时 workflow，也就是直接把 `Steps` 传给 `ExecuteSkill`。

如果这个流程以后会经常用，就可以先保存，再调用。

### 第一步：保存 workflow

调用 `SaveSkillAction`：

```json
{
  "SkillCode": "screenshot_and_wechat",
  "Remark": "截图后发送企业微信",
  "SkillType": "Workflow",
  "UpdateTime": "2026-04-09 10:00:00",
  "SkillActions": "[{\"Action\":\"screenshot_task\",\"Args\":{}},{\"Action\":\"wechat_task\",\"Args\":{\"action\":\"markdown\",\"content\":\"截图完成。\"}}]"
}
```

### 第二步：直接按 SkillCode 调用

```json
{
  "SkillCode": "screenshot_and_wechat",
  "Arguments": {}
}
```

### 说明
这时候就不用再传 `Steps` 了，因为步骤已经保存在数据库里了。

---

## 6. 最简单的“带参数”的工作流

这一步开始展示：

> workflow 保存的是模板，真正的值在调用时传入

### 示例：复制文件（参数化）

```json
{
  "SkillCode": "temp_copy_file",
  "Arguments": {
    "sourceFile": "D:\\test\\a.txt",
    "targetFile": "D:\\backup\\a.txt"
  },
  "Steps": [
    {
      "Action": "file_task",
      "Args": {
        "action": "copy",
        "from": "{{sourceFile}}",
        "to": "{{targetFile}}"
      }
    }
  ]
}
```

### 说明
这里：

- `sourceFile`
- `targetFile`

都不是写死在步骤里，而是从 `Arguments` 里传进去。

这样同一个 workflow 可以反复复用。

---

## 7. 一个稍微完整一点的参数化工作流示例

### 示例：调用本地工具处理 Excel，再发邮件

```json
{
  "SkillCode": "temp_excel_tool_process_notify",
  "Arguments": {
    "inputFile": "D:\\学生成绩.xlsx",
    "outputFile": "D:\\拆分结果.xlsx",
    "copyTo": "D:\\发送版\\拆分结果.xlsx",
    "emailTo": "mofeisi123@gmail.com",
    "toolPath": "D:\\LmyTools.exe"
  },
  "Steps": [
    {
      "Action": "tool_task",
      "Args": {
        "exePath": "{{toolPath}}",
        "arguments": "--input \"{{inputFile}}\" --output \"{{outputFile}}\"",
        "timeout": "60"
      }
    },
    {
      "Action": "file_task",
      "Args": {
        "action": "copy",
        "from": "{{outputFile}}",
        "to": "{{copyTo}}"
      }
    },
    {
      "Action": "email_task",
      "Args": {
        "to": "{{emailTo}}",
        "subject": "考试情况表处理结果",
        "body": "已按学校要求处理完成，附件见邮件。",
        "attachment": "{{step1.Data.to}}"
      }
    }
  ]
}
```

### 说明
这个 workflow 里同时用到了两种变量：

#### 1）输入参数
例如：

```text
{{inputFile}}
{{outputFile}}
{{emailTo}}
```

它们来自 `Arguments`。

#### 2）前一步结果
例如：

```text
{{step1.Data.to}}
```

它来自第 1 步（复制文件步骤）的执行结果。

---

## 8. 复杂一点的真实示例：登录办公系统，获取未读列表，并发送到企业微信

这个例子适合作为“真实业务 workflow 示例”。

### 临时调用示例

```json
{
  "SkillCode": "office_unread_list_and_wechat_preview",
  "Arguments": {
    "username": "刘明洋",
    "password": "Liumingyang@123",
    "loginUrl": "https://officea.caie.edu.cn/",
    "take": 10,
    "debug": false
  },
  "Steps": [
    {
      "Action": "browser_task",
      "Args": {
        "closeSession": false,
        "includeOutputs": false,
        "actions": [
          { "type": "goto", "url": "{{loginUrl}}" },
          { "type": "wait", "seconds": 1 },
          { "type": "fill", "selector": "#UserUid", "value": "{{username}}" },
          { "type": "fill", "selector": "#UserPwd", "value": "{{password}}" },
          { "type": "click_text", "value": "登 录" },
          { "type": "wait_url_contains", "value": "main", "timeoutMs": 15000 },
          { "type": "wait_for", "selector": "td.bright.tleft20.new", "timeoutMs": 15000 },
          { "type": "get_text_list", "selector": "td.bright.tleft20.new", "take": 10 }
        ]
      }
    },
    {
      "Action": "wechat_task",
      "Args": {
        "action": "markdown",
        "content": "内网未读消息检查完成。\\n未读数量：{{step0.Data.count}}\\n\\n{{step0.Text}}"
      }
    }
  ]
}
```

### 这个示例体现了什么

它体现了工作流最常见的 4 个能力：

#### 1）带输入参数
```text
{{username}}
{{password}}
{{loginUrl}}
```

#### 2）浏览器自动化
通过 `browser_task` 执行动作序列。

#### 3）步骤结果传递
```text
{{step0.Data.count}}
{{step0.Text}}
```

#### 4）后续通知
通过 `wechat_task` 把结果发出去。

---

## 9. 如果要把上面这个复杂示例保存成正式 workflow

### 第一步：调用 `SaveSkillAction`

```json
{
  "SkillCode": "office_check_unread_and_wechat",
  "Remark": "登录办公系统，获取未读消息列表，并将列表内容发送到企业微信",
  "SkillType": "Workflow",
  "UpdateTime": "2026-04-09 10:00:00",
  "SkillActions": "[{\"Action\":\"browser_task\",\"Args\":{\"closeSession\":\"false\",\"includeOutputs\":\"false\",\"actions\":[{\"type\":\"goto\",\"url\":\"{{loginUrl}}\"},{\"type\":\"wait\",\"seconds\":1},{\"type\":\"fill\",\"selector\":\"#UserUid\",\"value\":\"{{username}}\"},{\"type\":\"fill\",\"selector\":\"#UserPwd\",\"value\":\"{{password}}\"},{\"type\":\"click_text\",\"value\":\"登 录\"},{\"type\":\"wait_url_contains\",\"value\":\"main\",\"timeoutMs\":15000},{\"type\":\"wait_for\",\"selector\":\"td.bright.tleft20.new\",\"timeoutMs\":15000},{\"type\":\"get_text_list\",\"selector\":\"td.bright.tleft20.new\",\"take\":10}]}},{\"Action\":\"wechat_task\",\"Args\":{\"action\":\"markdown\",\"content\":\"内网未读消息检查完成。\\n未读数量：{{step0.Data.count}}\\n\\n{{step0.Text}}\"}}]"
}
```

### 第二步：以后直接调用

```json
{
  "SkillCode": "office_check_unread_and_wechat",
  "Arguments": {
    "username": "刘明洋",
    "password": "Liumingyang@123",
    "loginUrl": "https://officea.caie.edu.cn/",
    "take": 10,
    "debug": false
  }
}
```

---

## 10. 最后用一句话总结“怎么用工作流”

### 最简单调用
```json
{
  "SkillCode": "screenshot_task",
  "Arguments": {}
}
```

### 临时工作流
```json
{
  "SkillCode": "temp_demo",
  "Arguments": {},
  "Steps": [
    { "Action": "screenshot_task", "Args": {} },
    { "Action": "wechat_task", "Args": { "action": "markdown", "content": "完成" } }
  ]
}
```

### 先保存 workflow，再按 SkillCode 调用
```json
{
  "SkillCode": "office_check_unread_and_wechat",
  "Arguments": {
    "username": "...",
    "password": "...",
    "loginUrl": "..."
  }
}
```

---

## 11. 给未来自己的最简记忆版

记住这 3 句话就够了：

### 1）最简单调用
```text
SkillCode + Arguments
```

### 2）临时工作流
```text
SkillCode + Arguments + Steps
```

### 3）工作流里引用值
```text
输入参数：{{username}}
前一步结果：{{step0.Data.path}}
```
