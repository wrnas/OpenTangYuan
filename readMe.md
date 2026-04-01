# AI 技能框架使用说明

## 1. 框架目标

这套框架的目标是把系统能力整理成一组可被 AI 和其他程序调用的 WebAPI 技能，并支持两种执行方式：

1. **直接执行原子技能**
   - 例如：`browser_task`、`file_task`、`email_task`
2. **执行组合技能（workflow）**
   - 例如：先截图，再复制文件，最后发邮件

框架的核心思想是：

- 先复用已有技能
- 如果没有现成技能，再由 AI 组合原子技能执行
- 所有真正执行动作的入口统一走 `ExecuteSkill`

---

## 2. 核心能力结构

当前系统分为三层：

### 2.1 workflow 技能（数据库中保存）
数据库表 `Skills` 中保存的是现成的组合技能。

例如：

- `screen_send_mail`
- `office_login_and_send_email`

这些技能适合直接完成一个完整任务。

### 2.2 builtin 原子技能（代码内实现）
代码中内置了一组基础能力，例如：

- `browser_task`
- `file_task`
- `email_task`
- `screenshot_task`

这些技能适合被 AI 组合调用。

### 2.3 manifest（说明文件）
`Config/skill-manifest.json` 用于描述 builtin 技能的：

- 技能名称
- 用途说明
- 参数说明
- 调用示例

AI 通过读取 manifest，知道原子技能应该怎么调用。

---

## 3. AI 的推荐调用流程

### 3.1 先查询有哪些技能
调用：

- `GetSkillListForAI`

返回：

- `workflows`：数据库中的现成组合技能
- `builtins`：系统内置原子技能

### 3.2 如果有现成 workflow
AI 再调用：

- `GetSkillAction`

查看这个 workflow 的具体步骤，然后直接调用：

- `ExecuteSkill`

### 3.3 如果没有现成 workflow
AI 调用：

- `GetBuiltinSkillManifest`

查看 builtin skill 的参数结构和示例，然后自己组合 `Steps`，再调用：

- `ExecuteSkill`

---

## 4. 关键接口说明

### 4.1 GetSkillListForAI
作用：

- 返回 workflow 技能总览
- 返回 builtin 技能总览

典型用途：

- AI 判断“有没有现成技能可用”

### 4.2 GetBuiltinSkillManifest
作用：

- 返回 builtin 原子技能说明书

典型用途：

- AI 学习 `browser_task`、`file_task`、`email_task` 的参数结构

### 4.3 GetSkillAction
作用：

- 返回某个 workflow 技能的完整定义

典型用途：

- AI 查看某个现成 workflow 的具体步骤

### 4.4 SaveSkillAction
作用：

- 新增或更新数据库中的 workflow 技能

典型用途：

- 人工配置新的组合技能
- AI 自动生成 workflow 后保存入库

### 4.5 ExecuteSkill
作用：

- 统一执行入口

执行顺序：

1. 如果请求里直接带了 `Steps`，执行临时 workflow
2. 否则查数据库是否存在同名 workflow
3. 如果存在，按 workflow 执行
4. 如果不存在，按 builtin 原子技能执行

---

## 5. workflow 的执行机制

workflow 的核心是 `RunWorkflowAsync`。

执行特点：

1. 按顺序执行每个步骤
2. 上一步结果会写入上下文
3. 后续步骤可以通过模板变量引用前一步结果

支持的模板变量形式：

- `{{step0}}`
- `{{step0.path}}`
- `{{step0.data.path}}`
- `{{myInputVar}}`

例如：

```json
{
  "Action": "file_task",
  "Args": {
    "action": "copy",
    "from": "{{step0}}",
    "to": "C:\\Users\\admin\\Desktop\\a.png"
  }
}
```

如果 `step0` 返回的是一个对象，也可以这样写：

```json
{
  "Action": "email_task",
  "Args": {
    "to": "214634166@qq.com",
    "attachment": "{{step0.data.path}}"
  }
}
```

---

## 6. 直接执行原子技能示例

### 6.1 调用 `browser_task`
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

### 6.2 调用 `file_task`
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

### 6.3 调用 `email_task`
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

## 7. 直接执行临时组合技能示例

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
        "from": "{{step0}}",
        "to": "C:\\Users\\admin\\Desktop\\screen_test.png"
      }
    },
    {
      "Action": "email_task",
      "Args": {
        "to": "214634166@qq.com",
        "subject": "屏幕截图",
        "body": "这是自动截图邮件",
        "attachment": "C:\\Users\\admin\\Desktop\\screen_test.png"
      }
    }
  ]
}
```

---

## 8. 如何新增一个新的原子技能

新增原子技能时，建议按下面顺序修改。

### 8.1 第一步：实现实际功能方法
在 `SkillsController` 中新增一个方法，负责执行业务逻辑。

### 8.2 第二步：接入 `ExecuteSkillInternal`
把新技能注册到原子技能分发器中。

### 8.3 第三步：补充 manifest
在 `Config/skill-manifest.json` 中增加这个 skill 的说明，让 AI 知道怎么调用。

### 8.4 第四步：让 `GetSkillListForAI` 能看到它
如果 `GetSkillListForAI` 是从 manifest 读取 builtins，那么通常不需要额外改代码，只要 manifest 文件里有它就会自动出现。

### 8.5 第五步：补充示例和测试
至少准备一个 Swagger 调试示例，确认：

- 单独调用能成功
- 在 workflow 中能被后续步骤正确引用

---

## 9. 新增原子技能示例：文件重命名

下面举一个新增“文件重命名”能力的例子。

### 9.1 需求
新增一个 builtin skill：

- `rename_task`

功能：

- 把一个文件重命名为新名字

例如：

- 从 `C:\Temp\a.txt`
- 改成 `C:\Temp\b.txt`

---

### 9.2 在 `SkillsController` 中新增方法

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

    return new
    {
        success = true,
        oldPath = source,
        newPath = target,
        message = "重命名成功"
    };
}
```

---

### 9.3 修改 `ExecuteSkillInternal`

把 `rename_task` 加进去：

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

---

### 9.4 修改 `skill-manifest.json`

在 `skills` 数组里增加：

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

---

### 9.5 Swagger 测试示例

```json
{
  "SkillCode": "rename_task",
  "Arguments": {
    "from": "C:\\Temp\\a.txt",
    "to": "C:\\Temp\\b.txt"
  }
}
```

---

### 9.6 在 workflow 中使用

如果前一步生成了一个文件路径，后一步可以这样重命名：

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
        "from": "{{step0}}",
        "to": "C:\\Users\\admin\\Desktop\\renamed_screen.png"
      }
    }
  ]
}
```

---

## 10. 新增原子技能时要改哪些地方

最少要改这 3 处：

1. **新增业务方法**
   - 例如：`DoRenameTaskAsync`

2. **修改原子技能分发器**
   - `ExecuteSkillInternal`

3. **修改 manifest 文件**
   - `Config/skill-manifest.json`

通常这 3 处改完就够了。

如果这个技能有特殊安全要求，还要再检查：

4. **路径安全**
   - 是否需要经过 `ValidatePath`

5. **外部工具白名单**
   - 如果会调用 exe，是否需要加入白名单

---

## 11. 设计建议

### 11.1 尽量让返回结构稳定
为了让 AI 更容易串联步骤，建议原子技能尽量返回对象，而不是纯字符串。

推荐类似：

```json
{
  "success": true,
  "data": {
    "path": "C:\\Temp\\a.png"
  },
  "message": "操作成功"
}
```

这样后续步骤就可以稳定使用：

- `{{step0.data.path}}`

### 11.2 manifest 要写清楚
manifest 至少要包含：

- `skillCode`
- `desc`
- `args`
- `example`

### 11.3 workflow 优先
有现成 workflow 时，优先直接调用。这样：

- 更稳定
- 更省 token
- 更少出错

---

## 12. 当前框架已经具备的能力总结

目前这套框架已经实现了：

1. **内置原子技能执行**
2. **数据库 workflow 技能执行**
3. **直接临时 workflow 执行**
4. **步骤间模板变量传值**
5. **AI 可读的技能总览**
6. **AI 可读的 builtin manifest**
7. **AI 可读的 workflow 详情**
8. **统一的执行入口**
9. **路径/工具等安全限制**
10. **人工和 AI 都能复用同一套能力**

这意味着：

- AI 可以先查有没有现成技能
- 有就直接执行
- 没有就组合原子技能
- 后续步骤可以使用前一步结果

---

## 13. 推荐的 AI 使用顺序

```text
1. 调用 GetSkillListForAI，查看 workflows 和 builtins。
2. 如果 workflows 中已有可直接完成任务的技能，优先调用 GetSkillAction 查看详情，再调用 ExecuteSkill。
3. 如果没有合适 workflow，再调用 GetBuiltinSkillManifest 查看 builtin skill 的调用方式。
4. 组织 Arguments 或 Steps，调用 ExecuteSkill。
```

---

## 14. 总结

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
- 支持原子技能组合
- 易于扩展

如果后续继续增强：

- 返回结构进一步统一
- 提供更多示例
- 增加可视化编排

这套框架会非常适合做企业级 AI Agent 后端。
