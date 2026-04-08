# AI 技能框架使用说明（更新版）

## 1. 框架目标

这套框架的目标是把系统能力整理成一组可被 AI 和其他程序调用的 WebAPI 技能，并支持两种执行方式：

1. **直接执行原子技能（builtin）**
   - 例如：`browser_task`、`file_task`、`email_task`
2. **执行组合技能（workflow）**
   - 例如：先截图，再复制文件，最后发邮件

核心思想：

- 优先复用现成 workflow
- 如果没有现成 workflow，再由 AI 组合 builtin skill
- 所有真正执行动作的入口统一走 `ExecuteSkill`

---

## 2. 当前框架结构

### 2.1 workflow 技能
保存在数据库 `Skills` 表中，适合直接完成一个完整任务。

例如：

- `screen_send_mail`
- `office_login_and_send_email`

### 2.2 builtin 原子技能
在代码中实现，适合 AI 自己组合。

例如：

- `browser_task`
- `file_task`
- `email_task`
- `screenshot_task`

### 2.3 manifest 说明文件
文件位置：

```text
Config/skill-manifest.json
```

作用：

- 描述 builtin skill 的用途
- 描述参数结构
- 给出调用示例

AI 通过它知道原子技能怎么调用。

---

## 3. AI 的推荐调用流程

### 3.1 先查询技能总览
调用：

- `GetSkillListForAI`

返回：

- `workflows`
- `builtins`

### 3.2 如果有现成 workflow
再调用：

- `GetSkillAction`

查看这个 workflow 的具体步骤，然后调用：

- `ExecuteSkill`

### 3.3 如果没有现成 workflow
调用：

- `GetBuiltinSkillManifest`

查看 builtin skill 的参数结构和示例，然后自己组合 `Steps`，再调用：

- `ExecuteSkill`

---

## 4. 关键接口

### 4.1 GetSkillListForAI
返回：

- 数据库中的 workflow 技能
- manifest 文件中的 builtin skill

作用：

- AI 判断“有没有现成技能可直接用”

### 4.2 GetBuiltinSkillManifest
返回：

- `skill-manifest.json` 原始内容

作用：

- AI 学习 builtin skill 的调用方式

### 4.3 GetSkillAction
返回某个 workflow 技能的详细定义，包括：

- `skillCode`
- `remark`
- `skillType`
- `updateTime`
- `skillActionsRaw`
- `steps`

作用：

- AI 查看某个现成 workflow 的具体步骤

### 4.4 SaveSkillAction
作用：

- 新增或更新数据库中的 workflow 技能

### 4.5 ExecuteSkill
统一执行入口。

执行顺序：

1. 如果请求里直接带了 `Steps`，执行临时 workflow
2. 否则查数据库是否存在同名 workflow
3. 如果存在，执行 workflow
4. 如果不存在，执行 builtin skill

### 4.6 关于浏览器操作
browser_task 使用规则：

1. 优先使用标准动作：
goto, click, fill, press, select_option,
wait, wait_for, wait_for_load_state, wait_url_contains,
get_text, get_text_list, get_attr, exists, count,
screenshot, screenshot_element, download,
new_tab, switch_tab, get_tabs。

2. 一个 browser_task 尽量完成一整段浏览器流程，不要无故拆成多个 browser_task。

3. 读取页面信息前必须先等待页面稳定，优先使用 wait_for，少用固定 wait。

4. 点击会导致跳转时，优先设置 waitForNavigation=true，并指定 waitUntil。

5. 默认使用简单、稳定、可维护的 selector，不优先使用复杂文本匹配或 evaluate。

6. 登录、查询、下载、截图是 browser_task 最适合的场景；文件处理、邮件、微信通知应交给其他 skill。

7. 默认 onError=stop；只有可选步骤才使用 onError=skip。

8. 默认 closeSession=true；只有需要跨调用保留登录态时才使用 closeSession=false。


---

## 5. workflow 执行机制

workflow 的核心是：

- `RunWorkflowAsync`
- `ResolveTemplateVariables`

特点：

1. 按顺序执行每个步骤
2. 上一步结果写入上下文
3. 后续步骤可以通过模板变量引用前一步结果

支持的模板形式：

- `{{step0}}`
- `{{step0.path}}`
- `{{step0.data.path}}`
- `{{myInputVar}}`

推荐写法：

- **优先使用 `{{stepX.data.xxx}}`**
- 不推荐长期依赖 `{{stepX}}` 这种“整个对象/整个字符串”的写法

---

## 6. 统一 builtin 返回结构

为了让 AI 更稳定地组合步骤，builtin skill 的返回值应尽量统一为以下结构：

```json
{
  "success": true,
  "skillCode": "file_task",
  "type": "copy",
  "text": "已复制",
  "data": {
    "from": "C:\\Temp\\a.png",
    "to": "D:\\a.png"
  },
  "error": ""
}
```

推荐模型：

```csharp
public class SkillResult
{
    public bool Success { get; set; } = true;
    public string SkillCode { get; set; } = "";
    public string Type { get; set; } = "";
    public string Text { get; set; } = "";
    public object? Data { get; set; }
    public string Error { get; set; } = "";
}
```

### 统一后的好处

1. AI 更容易理解返回值
2. 后续步骤更容易引用前一步结果
3. 日志和调试更清晰
4. 不同 builtin skill 的行为更一致

### 推荐规则

- `text`：给人看
- `data`：给后续步骤引用
- 后续步骤优先取 `data.xxx`

例如：

```json
"from": "{{step0.data.path}}"
"attachment": "{{step1.data.to}}"
```

---

## 7. 推荐统一的几个 builtin

建议优先统一这几个：

1. `screenshot_task`
2. `file_task`
3. `email_task`
4. `browser_task`（至少统一常用动作的 `data`）

---

## 8. builtin 调用示例

### 8.1 browser_task
```json
{
  "SkillCode": "browser_task",
  "Arguments": {
    "actions": [
      { "type": "goto", "url": "https://www.baidu.com" },
      { "type": "get_text", "selector": "title" }
    ]
  }
}
```

### 8.2 file_task
```json
{
  "SkillCode": "file_task",
  "Arguments": {
    "action": "copy",
    "from": "C:\\Temp\\a.png",
    "to": "D:\\a.png"
  }
}
```

### 8.3 email_task
```json
{
  "SkillCode": "email_task",
  "Arguments": {
    "to": "214634166@qq.com",
    "subject": "测试邮件",
    "body": "测试正文",
    "attachment": "D:\\a.png"
  }
}
```

---

## 9. 临时 workflow 调用示例

如果不想先存数据库，可以直接在 `ExecuteSkill` 中传 `Steps`。

示例：截图 → 复制到桌面 → 发邮件

```json
{
  "SkillCode": "temp_screen_copy_mail",
  "Arguments": {},
  "Steps": [
    {
      "Action": "screenshot_task",
      "Args": {}
    },
    {
      "Action": "file_task",
      "Args": {
        "action": "copy",
        "from": "{{step0.data.path}}",
        "to": "C:\\Users\\admin\\Desktop\\screen_test.png"
      }
    },
    {
      "Action": "email_task",
      "Args": {
        "to": "214634166@qq.com",
        "subject": "屏幕截图",
        "body": "这是自动截图邮件",
        "attachment": "{{step1.data.to}}"
      }
    }
  ]
}
```

---

## 10. 如何新增一个原子技能

新增原子技能时，建议按这个顺序修改。

### 10.1 新增业务方法
在 `SkillsController` 中实现实际功能方法。

### 10.2 修改 ExecuteSkillInternal
把新 skill 注册到 builtin 分发器中。

### 10.3 修改 manifest 文件
在 `Config/skill-manifest.json` 中增加说明。

### 10.4 准备 Swagger 示例
至少准备一个单独调用示例，以及一个 workflow 中使用示例。

---

## 11. 新增原子技能示例：文件重命名

目标：

- 新增 builtin skill：`rename_task`

功能：

- 把一个文件重命名为新文件名

### 11.1 新增业务方法

```csharp
/// <summary>
/// 文件重命名
///
/// 参数：
/// - from: 原文件完整路径
/// - to: 新文件完整路径
/// </summary>
private async Task<object> DoRenameTaskAsync(Dictionary<string, object> args)
{
    string from = GetString(args, "from");
    string to = GetString(args, "to");

    if (string.IsNullOrWhiteSpace(from))
        throw new ArgumentException("from 不能为空");

    if (string.IsNullOrWhiteSpace(to))
        throw new ArgumentException("to 不能为空");

    var source = ValidatePath(from, mustExist: true);
    var target = ValidatePath(to, mustExist: false);

    await Task.Run(() =>
    {
        var targetDir = Path.GetDirectoryName(target);
        if (!string.IsNullOrWhiteSpace(targetDir))
            Directory.CreateDirectory(targetDir);

        if (File.Exists(target))
            File.Delete(target);

        File.Move(source, target);
    });

    return new SkillResult
    {
        Success = true,
        SkillCode = "rename_task",
        Type = "rename",
        Text = "重命名成功",
        Data = new
        {
            from = source,
            to = target
        }
    };
}
```

### 11.2 修改 ExecuteSkillInternal

```csharp
private async Task<object> ExecuteSkillInternal(string skillCode, Dictionary<string, object>? args)
{
    var code = skillCode?.Trim().ToLowerInvariant() ?? "";
    var safeArgs = args ?? new Dictionary<string, object>();

    return code switch
    {
        "file_task" => await DoFileTaskAsync(safeArgs),
        "open_task" => await DoOpenTaskAsync(safeArgs),
        "print_task" => await DoPrintTaskAsync(safeArgs),
        "folder_task" => await DoFolderTaskAsync(safeArgs),
        "tool_task" => await RunExternalToolAsync(safeArgs),
        "lock_task" => await CommandTools.LockScreenAsync(),
        "screenshot_task" => await CommandTools.CaptureScreenAsync(),
        "email_task" => await CommandTools.SendEmailAsync(safeArgs),
        "browser_task" => await DoBrowserTaskAsync(safeArgs),
        "rename_task" => await DoRenameTaskAsync(safeArgs),
        _ => throw new NotSupportedException($"不支持的技能：{skillCode}")
    };
}
```

### 11.3 修改 skill-manifest.json

```json
{
  "skillCode": "rename_task",
  "desc": "重命名文件",
  "args": {
    "from": "原文件完整路径，必填",
    "to": "新文件完整路径，必填"
  },
  "example": {
    "SkillCode": "rename_task",
    "Arguments": {
      "from": "C:\\Temp\\a.txt",
      "to": "C:\\Temp\\b.txt"
    }
  }
}
```

### 11.4 Swagger 调试示例

```json
{
  "SkillCode": "rename_task",
  "Arguments": {
    "from": "C:\\Temp\\a.txt",
    "to": "C:\\Temp\\b.txt"
  }
}
```

### 11.5 在 workflow 中使用

```json
{
  "SkillCode": "temp_rename_test",
  "Arguments": {},
  "Steps": [
    {
      "Action": "screenshot_task",
      "Args": {}
    },
    {
      "Action": "rename_task",
      "Args": {
        "from": "{{step0.data.path}}",
        "to": "C:\\Users\\admin\\Desktop\\renamed_screen.png"
      }
    }
  ]
}
```

---

## 12. 新增原子技能时最少要改的地方

最少改这 3 处：

1. 新增业务方法
2. 修改 `ExecuteSkillInternal`
3. 修改 `Config/skill-manifest.json`

如果涉及安全控制，还要额外检查：

4. 路径是否需要走 `ValidatePath`
5. 是否涉及 exe 白名单
6. 是否需要额外日志

---

## 13. 推荐设计原则

### 13.1 返回值尽量稳定
重要结果尽量放在 `data` 中。

例如：

- 截图技能 → `data.path`
- 文件复制 → `data.to`
- 邮件发送 → `data.to`
- 浏览器下载 → `data.path`

### 13.2 manifest 要简短清楚
manifest 至少要包含：

- `skillCode`
- `desc`
- `args`
- `example`

### 13.3 workflow 优先
如果有现成 workflow，优先直接调用。原因：

- 更稳定
- 更省 token
- 更少出错

---

## 14. 当前框架已经实现的能力

目前这套框架已经支持：

1. builtin skill 执行
2. 数据库 workflow 执行
3. 临时 workflow 执行
4. 步骤间模板变量传值
5. AI 可读的技能总览
6. AI 可读的 builtin manifest
7. AI 可读的 workflow 详情
8. 统一执行入口
9. 路径 / 工具等安全限制
10. 人工和 AI 共用同一套能力

---

## 15. 推荐的 AI 使用顺序

```text
1. 调用 GetSkillListForAI，查看 workflows 和 builtins。
2. 如果 workflows 中已有可直接完成任务的技能，优先调用 GetSkillAction 查看详情，再调用 ExecuteSkill。
3. 如果没有合适 workflow，再调用 GetBuiltinSkillManifest 查看 builtin skill 的调用方式。
4. 组织 Arguments 或 Steps，调用 ExecuteSkill。
```

---

## 16. 总结

这套框架本质上是一个统一的 AI 技能执行平台。

它适合：

- 给 AI 调用
- 给前端调用
- 给其他程序调用
- 做企业内部自动化平台

它的优势在于：

- 能力统一
- 权限可控
- 支持 workflow
- 支持 builtin skill 组合
- 易于扩展

这次更新后，builtin 返回结构进一步统一，AI 在组合步骤时会更稳定，后续引用上一步结果也会更自然。
