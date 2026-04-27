
## 注意事项
### 发布docker后更新程序。
```cd /www/wwwroot/OpenTangYuanDocker
```docker-compose up -d --build


### 后台全部返回统一格式：
```c#
public int code { get; set; }
public string message { get; set; }
public T data { get; set; }
前台使用拦截器后真正返回的是 data中的数据。
拦截器在 config/request.js 
```
后台向前台返回数据，统一如下设置
```c#
// 获取用户详细信息
var userData = await GetUserWithRolesInternal(userId);
var result = (new
{
    AccessToken = accessToken,
    RefreshToken = refreshToken,
    UserInfo = userData
});

return Ok(ResponseHelper.Success(result, "登录成功"));  
也可以使用基类的方法：
return HandleSuccess(reslut);
```
否则前台会全部拦截，无法处理。

### 浏览器操作，ai可用动作 2026-03-16
你可以调用 Browser 工具控制浏览器。

接口：
POST /AiApi/Browser/run

请求格式：

{
  "sessionId": "可选",
  "actions": []
}

每个 action 包含：

type
selector
value
url
attr

常用动作：

goto
打开网页

click
点击元素

click_text
按文字点击

fill
输入文本

wait
等待

wait_for
等待元素

get_text
获取文本

get_attr_list
获取属性列表

evaluate
执行JavaScript

screenshot
截图

download
下载文件

如果 selector 不确定，可以先使用 analyze_page。



.json文件中要增加
"Browser": {
  "Headless": true,
  "DefaultTimeoutMs": 30000
},
"BrowserSecurity": {
  "EnableDomainCheck": true,
  "AllowedDomains": [
    "*.*"
  ]
}



### 统一请求
为了统一请求方式，后期所有请求参考这种写法。
```javascript
export const login = async (UserCode, Password) => {
  const loginData = { UserCode, Password }
  // 这里如果需要认证，则传 custom: { auth: true }  
  const res = await uni.$u.http.post('/Authorization/LoginWithToken', loginData)  
  return res.UserInfo
}
```


