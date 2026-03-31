你可以调用我的 WebAPI 工具完成文件操作、打印、打开、归类和执行外部工具。

所有工具使用统一格式：
{
  "SkillCode": "工具名",
  "Arguments": { 参数键值对 }
}

------------------------------------
【工具1：file_task】
用途：执行文件搜索、复制、移动、创建文件夹
支持 action：
- search 搜索
  参数：keyword、ext
  示例：
  {"SkillCode":"file_task","Arguments":{"action":"search","keyword":"new0106","ext":"txt"}}

- copy 复制
  参数：from、to
  示例：
  {"SkillCode":"file_task","Arguments":{"action":"copy","from":"源路径","to":"目标路径"}}

- move 移动
  参数：from、to

- mkdir 创建文件夹
  参数：from（路径）

------------------------------------
【工具2：open_task】
用途：打开文件/程序/文件夹
参数：path
示例：
{"SkillCode":"open_task","Arguments":{"path":"D:\\new0106.txt"}}

------------------------------------
【工具3：print_task】
用途：打印 Word/Excel/PDF/图片
参数：path
示例：
{"SkillCode":"print_task","Arguments":{"path":"D:\\new0106.txt"}}

------------------------------------
【工具4：folder_task】
用途：按后缀自动归类文件
参数：source
示例：
{"SkillCode":"folder_task","Arguments":{"source":"D:\\test"}}

------------------------------------
【工具5：tool_task】
用途：调用外部命令行 exe
参数：exePath、arguments、timeout
示例：
{"SkillCode":"tool_task","Arguments":{"exePath":"C:\\sendmail.exe","arguments":"test","timeout":10}}

------------------------------------
使用规则：
1. 用户需求简单 → 直接调用对应工具
2. 需要多步操作 → 调用技能库（SkillCode 为预定义的技能编码）
3. 不允许调用未在列表中的工具





文件助手 API 自用备忘录
一、接口统一调用规则
接口地址：
plaintext
/api/Skills/ExecuteSkill
通用请求体结构：
json
{
  "SkillCode": "工具名或技能库编码",
  "Arguments": {
    "参数名": "参数值"
  }
}
二、Swagger 直接调试示例（原子操作）
2.1 搜索文件
json
{
  "SkillCode": "file_task",
  "Arguments": {
    "action": "search",
    "keyword": "new0106",
    "ext": "txt"
  }
}
2.2 复制文件
json
{
  "SkillCode": "file_task",
  "Arguments": {
    "action": "copy",
    "from": "C:\\xxx\\new0106.txt",
    "to": "D:\\new0106.txt"
  }
}
2.3 移动文件
json
{
  "SkillCode": "file_task",
  "Arguments": {
    "action": "move",
    "from": "源路径",
    "to": "目标路径"
  }
}
2.4 创建文件夹
json
{
  "SkillCode": "file_task",
  "Arguments": {
    "action": "mkdir",
    "from": "D:\\test_folder"
  }
}
2.5 打开文件 / 程序 / 目录
json
{
  "SkillCode": "open_task",
  "Arguments": {
    "path": "D:\\new0106.txt"
  }
}
2.6 打印文件
json
{
  "SkillCode": "print_task",
  "Arguments": {
    "path": "D:\\new0106.txt"
  }
}
2.7 按后缀自动归类文件
json
{
  "SkillCode": "folder_task",
  "Arguments": {
    "source": "D:\\待整理目录"
  }
}
2.8 调用外部命令行工具
json
{
  "SkillCode": "tool_task",
  "Arguments": {
    "exePath": "C:\\sendmail.exe",
    "arguments": "命令行参数",
    "timeout": 10
  }
}
三、技能库使用说明
3.1 数据库字段
SkillCode：技能唯一标识
SkillActions：步骤 JSON 数组
Remark：功能说明
3.2 支持的 Action
search
copy
move
print
open
mkdir
tool
3.3 步骤变量引用
{{step0}} 第 0 步执行结果
{{step1}} 第 1 步执行结果
以此类推
四、常用技能库模板
4.1 搜索并复制到 D 盘
SkillCode：
plaintext
search_copy_to_d
SkillActions：
json
[
  {
    "Action": "search",
    "Args": {
      "keyword": "new0106",
      "ext": "txt"
    }
  },
  {
    "Action": "copy",
    "Args": {
      "from": "{{step0}}",
      "to": "D:\\new0106.txt"
    }
  }
]
调用方式：
json
{
  "SkillCode": "search_copy_to_d",
  "Arguments": {}
}
4.2 搜索 → 复制 → 打印
SkillCode：
plaintext
search_copy_print
SkillActions：
json
[
  {
    "Action": "search",
    "Args": {
      "keyword": "2025总结",
      "ext": "docx"
    }
  },
  {
    "Action": "copy",
    "Args": {
      "from": "{{step0}}",
      "to": "D:\\2025总结.docx"
    }
  },
  {
    "Action": "print",
    "Args": {
      "path": "D:\\2025总结.docx"
    }
  }
]
五、重要注意事项
路径必须用双反斜杠：\\
外部 exe 必须在白名单内：sendmail.exe、pdf2txt.exe、magick.exe
搜索自动降级：Everything → 系统遍历
删除操作已禁用，无法调用
技能库的 JSON 步骤不能直接粘贴到 Swagger，必须存在数据库
Swagger 只允许传入 SkillCode + Arguments，不允许传数组