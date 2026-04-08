# 教务自动化场景专用 README

---

# 1. 这份文档是干什么的

这份文档不是讲框架原理，而是专门说明：

> **如何把当前 AI 技能框架，落地到教务自动化场景中**

适合的使用者：

* 未来的我自己
* 负责维护这套系统的人
* 需要给 AI 提供教务自动化能力的人

---

# 2. 教务自动化的目标

本项目在教务场景中的目标，不是做一个“大而全”的平台，而是：

> **把重复、规则明确、又经常要人工点来点去的工作，做成可执行的技能和工作流**

重点解决这几类问题：

* 登录教务系统并检查通知
* 导出成绩、名单、报表
* 下载 Excel / 附件
* 调用本地工具处理文件
* 文件整理、复制、归档
* 通过邮件或企业微信发送处理结果
* 对关键页面截图留痕

---

# 3. 教务自动化最适合做的事情

适合自动化的场景：

## 3.1 查询类

* 查询是否有未读通知
* 查询待审核名单
* 查询考试安排
* 查询某类学生信息
* 查询页面中是否存在异常提示

## 3.2 导出类

* 导出成绩表
* 导出考试安排
* 导出学生名单
* 下载系统附件

## 3.3 文件处理类

* 复制、移动、重命名文件
* 调用本地 exe 工具处理 Excel
* 将结果归档到指定目录

## 3.4 通知类

* 发邮件
* 发企业微信
* 发截图说明
* 发处理结果说明

---

# 4. 教务自动化不适合做的事情

这些事情不建议一开始就自动化：

* 强依赖验证码识别的复杂登录
* 页面结构极不稳定的系统
* 完全没有规则、每次都不同的人工判断
* 高风险删除、批量修改、批量提交类操作
* 需要强业务确认才能执行的流程

原则：

> **先做“查、导、下、发、留痕”**
>
> 不要一开始做“批量改、批量删、批量提交”

---

# 5. 教务自动化推荐 skill 分工

## 5.1 browser_task

负责：

* 打开页面
* 登录
* 查询
* 读取结果
* 截图
* 下载文件

不负责：

* 文件复制
* 文件重命名
* Excel 处理
* 邮件发送
* 微信发送

---

## 5.2 file_task

负责：

* search
* copy
* move
* rename
* mkdir

---

## 5.3 tool_task

负责：

* 调用本地 exe 工具
* 例如：

  * 拆分成绩表
  * 生成新 Excel
  * 格式转换

---

## 5.4 email_task

负责：

* 发送结果邮件
* 发送附件
* 给自己或指定老师发通知

---

## 5.5 wechat_task

负责：

* 发送企业微信机器人通知
* 文本
* markdown
* card

---

# 6. 教务自动化设计原则

---

## 6.1 一个 workflow 只做一件完整的事

例如：

* 检查未读通知并提醒
* 导出成绩并发送
* 下载报表并归档

不要把无关流程硬拼在一起。

---

## 6.2 先查询，再留痕，再通知

推荐顺序：

```text
查询 → 判断 → 截图/下载 → 文件处理 → 发送通知
```

---

## 6.3 关键结果一定要放 data

例如：

* 浏览器下载 → `data.path`
* 截图 → `data.path`
* 文件复制 → `data.to`
* 工具处理输出文件 → `data.outputPath`（建议）

后续 workflow 统一引用：

```text
{{stepX.data.xxx}}
```

---

## 6.4 browser_task 尽量一次完成一段流程

例如登录后读取页面结果：

```text
goto → wait_for → fill → fill → click → wait_for → get_text
```

不要无故拆成多个 `browser_task`。

---

## 6.5 教务场景优先保守执行

默认策略：

* `onError = stop`
* `closeSession = true`

只有明确要跨多次调用时才使用：

* `closeSession = false`

---

# 7. 教务自动化常见 workflow 模板

下面这些模板是最值得优先沉淀的。

---

# 8. 模板 1：检查教务系统未读通知并发送提醒

## 8.1 适用场景

每天检查教务系统通知中心，如果有未读消息，则截图并发企业微信提醒。

---

## 8.2 推荐流程

```text
browser_task
→ wechat_task
```

---

## 8.3 示例（临时 workflow）

```json
{
  "SkillCode": "temp_check_notice_and_wechat",
  "Arguments": {},
  "Steps": [
    {
      "Action": "browser_task",
      "Args": {
        "closeSession": true,
        "actions": [
          { "type": "goto", "url": "http://教务系统地址" },
          { "type": "wait_for", "selector": "#username" },
          { "type": "fill", "selector": "#username", "value": "账号" },
          { "type": "fill", "selector": "#password", "value": "密码" },
          { "type": "click", "selector": "#loginBtn", "waitForNavigation": true, "waitUntil": "domcontentloaded" },
          { "type": "wait_for", "selector": ".msg-center" },
          { "type": "click", "selector": ".msg-center" },
          { "type": "wait_for", "selector": ".unread-count" },
          { "type": "get_text", "selector": ".unread-count" },
          { "type": "screenshot_element", "selector": "#noticeArea" }
        ]
      }
    },
    {
      "Action": "wechat_task",
      "Args": {
        "action": "markdown",
        "content": "教务系统通知检查完成。\\n未读信息请及时查看。\\n截图位置：<font color=\"warning\">{{step0.data.data.path}}</font>"
      }
    }
  ]
}
```

---

# 9. 模板 2：导出成绩表 → 调工具处理 → 发邮件

## 9.1 适用场景

从教务系统导出成绩表，调用本地工具拆分/处理，然后发到指定邮箱。

---

## 9.2 推荐流程

```text
browser_task
→ tool_task
→ email_task
```

---

## 9.3 示例

```json
{
  "SkillCode": "temp_export_score_and_send_mail",
  "Arguments": {},
  "Steps": [
    {
      "Action": "browser_task",
      "Args": {
        "closeSession": true,
        "actions": [
          { "type": "goto", "url": "http://教务系统地址/score" },
          { "type": "wait_for", "selector": "#exportBtn" },
          { "type": "download", "selector": "#exportBtn", "fileName": "学生成绩.xlsx" }
        ]
      }
    },
    {
      "Action": "tool_task",
      "Args": {
        "exePath": "D:\\LmyTools.exe",
        "arguments": "--input \"{{step0.data.data.path}}\" --output \"D:\\拆分结果.xlsx\"",
        "timeout": "60"
      }
    },
    {
      "Action": "email_task",
      "Args": {
        "to": "你的邮箱@example.com",
        "subject": "成绩表处理结果",
        "body": "成绩表已处理完成，详见附件。",
        "attachment": "D:\\拆分结果.xlsx"
      }
    }
  ]
}
```

---

# 10. 模板 3：导出考试安排并归档

## 10.1 适用场景

下载考试安排表后，复制到固定目录做归档。

---

## 10.2 推荐流程

```text
browser_task
→ file_task
```

---

## 10.3 示例

```json
{
  "SkillCode": "temp_exam_schedule_archive",
  "Arguments": {},
  "Steps": [
    {
      "Action": "browser_task",
      "Args": {
        "closeSession": true,
        "actions": [
          { "type": "goto", "url": "http://教务系统地址/exam" },
          { "type": "wait_for", "selector": "#exportBtn" },
          { "type": "download", "selector": "#exportBtn", "fileName": "考试安排.xlsx" }
        ]
      }
    },
    {
      "Action": "file_task",
      "Args": {
        "action": "copy",
        "from": "{{step0.data.data.path}}",
        "to": "D:\\教务归档\\考试安排.xlsx"
      }
    }
  ]
}
```

---

# 11. 模板 4：查询某类名单并截图留痕

## 11.1 适用场景

查询待审核、待确认、异常名单，截图后发通知。

---

## 11.2 推荐流程

```text
browser_task
→ wechat_task 或 email_task
```

---

## 11.3 示例

```json
{
  "SkillCode": "temp_check_pending_list",
  "Arguments": {},
  "Steps": [
    {
      "Action": "browser_task",
      "Args": {
        "closeSession": true,
        "actions": [
          { "type": "goto", "url": "http://教务系统地址/pending" },
          { "type": "wait_for", "selector": ".result-row" },
          { "type": "count", "selector": ".result-row" },
          { "type": "screenshot_element", "selector": "#resultArea" }
        ]
      }
    },
    {
      "Action": "wechat_task",
      "Args": {
        "action": "text",
        "content": "待处理名单检查完成，截图已生成：{{step0.data.data.path}}"
      }
    }
  ]
}
```

---

# 12. 模板 5：系统巡检日报

## 12.1 适用场景

每天固定检查几个页面是否正常打开、是否有异常提示，并发日报。

---

## 12.2 推荐流程

```text
browser_task
→ browser_task
→ wechat_task / email_task
```

也可以合并成一个 browser_task。

---

## 12.3 示例思路

```text
检查登录页
检查通知页
检查成绩页
如果任一页面异常，则截图并通知
```

---

# 13. browser_task 在教务场景中的推荐写法

## 13.1 登录流程标准写法

```text
goto
→ wait_for(username)
→ fill(username)
→ fill(password)
→ click(loginBtn, waitForNavigation=true)
→ wait_for(登录成功标志)
```

---

## 13.2 查询流程标准写法

```text
fill(查询条件)
→ click(查询按钮)
→ wait_for(结果区域)
→ get_text / get_text_list / count
```

---

## 13.3 截图流程标准写法

```text
wait_for(目标区域)
→ screenshot_element
```

---

## 13.4 下载流程标准写法

```text
wait_for(导出按钮)
→ download
```

---

# 14. 教务 workflow 命名建议

建议统一命名风格：

```text
模块_动作_结果
```

例如：

* `notice_check_and_wechat`
* `score_export_process_and_send_mail`
* `exam_schedule_export_and_archive`
* `pending_list_check_and_screenshot`

这样以后你自己一眼就能知道这个 workflow 干什么。

---

# 15. 教务自动化中的安全建议

## 15.1 默认只做“读”和“导”

优先：

* 查询
* 读取
* 导出
* 下载
* 截图
* 通知

谨慎做：

* 修改
* 删除
* 批量提交

---

## 15.2 文件路径要受控

涉及文件时，必须确认：

* `AllowedRoots` 包含目标目录
* 目标路径存在或可创建

---

## 15.3 外部工具必须走白名单

使用 `tool_task` 时，必须确认：

* exe 已加入 `AllowedExeNames`
* 参数是安全的
* 输出路径是允许的

---

## 15.4 浏览器动作优先用标准动作

优先：

* goto
* click
* fill
* wait_for
* get_text
* screenshot
* download

不建议默认用：

* evaluate
* 复杂页面分析
* 复杂 JS 注入

---

# 16. 我后续维护时的优先级建议

优先做这 5 件事：

1. 固化 5 个高频教务 workflow
2. 统一所有 builtin 返回结构
3. 让 `browser_task` 的结果更稳定
4. 把下载文件路径、截图路径放进 `data`
5. 把常见流程都做成模板

---

# 17. 判断一个教务场景是否值得做成 workflow

满足以下 3 条，基本就值得做：

* 经常重复
* 规则清楚
* 结果可以标准化

例如：

* 每天查通知
* 每周导出考试安排
* 每次导出成绩后都要拆分并发邮件

---

# 18. 最后总结

这套系统在教务场景中的定位不是“大平台”，而是：

> **一个本地可控、可编排、可被 AI 调用的教务自动化执行引擎**

现在最重要的不是继续加很多功能，而是：

* 沉淀高频 workflow
* 固化标准动作
* 保持返回结构稳定
* 让未来的自己一眼就能看懂
