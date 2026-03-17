### 动作说明
页面导航
goto
作用：打开网页
必填：url
示例：
{ "type": "goto", "url": "https://news.163.com" }

✅ 页面交互
click

作用：点击元素

必填：selector

注意：点击后可能需要 wait

{ "type": "click", "selector": ".btn" }


click_text

作用：按文字点击（比 selector 更通用）

{ "type": "click_text", "value": "登录" }


fill

作用：输入文本

{ "type": "fill", "selector": "#username", "value": "admin" }

✅ 等待（非常重要）

wait

固定等待

{ "type": "wait", "seconds": 2 }


wait_for

等元素出现（推荐）

{ "type": "wait_for", "selector": ".list-item" }

✅ 数据抓取

get_text

获取一个元素

{ "type": "get_text", "selector": "h1" }


get_text_list

获取多个元素（最常用）

{ "type": "get_text_list", "selector": ".news-title" }


get_links_structured

获取标题 + 链接

{ "type": "get_links_structured", "selector": "a" }

✅ 判断类（AI决策关键）

exists

判断元素是否存在

{ "type": "exists", "selector": ".login-btn" }

✅ 页面分析

analyze_page

分析按钮 / 输入框 / 链接

{ "type": "analyze_page" }

✅ 内容提取

extract_article

提取文章

{ "type": "extract_article" }


### AI 使用说明

你可以使用 Browser 工具来操作网页。

请使用 actions 数组描述操作步骤，每一步必须包含 type。

常用规则：

打开网页使用 goto

页面加载后再操作，必要时使用 wait 或 wait_for

点击元素使用 click 或 click_text

获取数据优先使用 get_text_list

如果不确定页面结构，可以先用 analyze_page

如果需要判断元素是否存在，使用 exists

多步骤任务必须拆成多个 actions 顺序执行

示例：

{
  "actions": [
    { "type": "goto", "url": "https://example.com" },
    { "type": "wait_for", "selector": ".item" },
    { "type": "get_text_list", "selector": ".item" }
  ]
}
