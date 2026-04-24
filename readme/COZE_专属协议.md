# Coze 统一返回 JSON 协议

适用接口：`POST /api/Skills/ExecuteSkillForCoze`

## 顶层结构

```json
{
  "success": true,
  "message": "执行成功",
  "skillCode": "office_check_unread",
  "executeMode": "workflow",
  "resultType": "text_list",
  "resultText": "消息1\n消息2",
  "resultList": ["消息1", "消息2"],
  "resultData": {},
  "sessionId": "xxxx",
  "page": {
    "url": "https://officea.caie.edu.cn/main",
    "title": "办公系统"
  },
  "session": {
    "sessionId": "xxxx",
    "reusable": true,
    "keepAliveSuggested": true,
    "timeoutMinutes": 30,
    "followUpHint": "后续同站点追问请继续复用这个 sessionId；除非用户明确要求结束，否则不要关闭会话。"
  },
  "needMoreInput": false,
  "missingArgs": [],
  "errorCode": "",
  "errorMessage": "",
  "debug": null
}
```

## 字段约定

- `success`：本次技能调用是否成功
- `message`：对本次调用的简短总结
- `skillCode`：执行的技能编码
- `executeMode`：`builtin | workflow | temp_workflow`
- `resultType`：结果类型，如 `text` / `text_list` / `article` / `workflow`
- `resultText`：最适合直接给用户展示的文本
- `resultList`：最适合模型继续枚举、排序、点选的数组
- `resultData`：原始结构化结果；后续动作尽量从这里取值
- `sessionId`：浏览器多轮会话 ID；非浏览器任务通常为空
- `page`：当前页 URL 和标题；便于后续追问
- `session`：浏览器会话状态
- `needMoreInput`：当前是否缺少参数，若为 true，智能体应先向用户索取 `missingArgs`
- `missingArgs`：缺少的参数列表
- `errorCode`：稳定错误码
- `errorMessage`：失败原因
- `debug`：仅调试时附带

## 智能体取值优先级

1. 是否成功：看 `success`
2. 直接回复用户：优先用 `resultText`
3. 继续选择列表项：优先用 `resultList`
4. 做结构化后续调用：优先用 `resultData`
5. 同网站后续追问：优先复用 `sessionId`
6. 若 `needMoreInput=true`：先向用户索取 `missingArgs`

## 多轮浏览器规则

- 第一次浏览器任务成功后，保存 `sessionId`
- 用户后续说“第一个”“刚才那个页面”“继续查看详情”，默认复用 `sessionId`
- 除非用户明确要求结束，或当前任务明确传了 `closeSession=true`，否则不要主动关闭会话
- 如果返回里 `session.reusable=false`，说明下一轮需要重新创建 session



# Coze 编排建议

## 目标
- 优先使用数据库中已有 workflow
- workflow 不满足时，继续自行组合 builtin skill
- 不要因为已有 workflow 就停止后续动作编排

## 推荐规则
1. 先调用 `GetSkillListForAI`
2. 如果已有 workflow 可以完成**部分或全部**任务，先执行它
3. 如果 workflow 只完成了前半段任务，继续基于返回结果调用 builtin skill
4. 浏览器场景中，只要返回了 `sessionId`，后续追问默认复用它
5. 当返回 `needMoreInput=true` 时，先向用户索取 `missingArgs`

## 例子：查看办公系统未读消息，然后发送到微信
### 正确策略
- 第一步：调用 `office_check_unread`
- 第二步：从返回里读取 `resultText` 或 `resultList`
- 第三步：调用 `wechat_task` 发送消息

### 示例二段式执行
第一步：
```json
{
  "SkillCode": "office_check_unread",
  "Arguments": {
    "username": "张三",
    "password": "***",
    "loginUrl": "https://officea.caie.edu.cn/",
    "take": 10
  }
}
```

第二步：
```json
{
  "SkillCode": "wechat_task",
  "Arguments": {
    "action": "markdown",
    "content": "办公系统未读消息如下：\n\n{{上一步的 resultText}}"
  }
}
```

## 例子：先列出未读，再查看第一条详情
第一步：
- 调用 `office_check_unread`
- 保存返回中的 `sessionId`

第二步：
```json
{
  "SkillCode": "browser_task",
  "Arguments": {
    "sessionId": "上一步返回的 sessionId",
    "closeSession": false,
    "actions": [
      { "type": "click_index", "selector": "td.bright.tleft20.new", "index": 0, "waitForNavigation": true },
      { "type": "extract_article" }
    ]
  }
}
```

