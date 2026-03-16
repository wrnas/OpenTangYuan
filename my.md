
## 注意事项
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

### 浏览器操作，ai可用动作
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

### coze 私钥
-----BEGIN PRIVATE KEY-----
MIIEvAIBADANBgkqhkiG9w0BAQEFAASCBKYwggSiAgEAAoIBAQDDZvhz0sbLLUGw
pXBMF27n6+R9V5xUzgF+9pihCUYG5rhSha2vSLdtyOikt6fIQMPso+XMXRCl2ckH
XhvT09nYgOPIvRIwxJ5Q+F2BiNoo4WZx18zjHDZiJhTg8vFP/e7kOf5RZK9m43CF
dEPe8Nt43xewLbX+n5pofPynjcTLV7RdzEX+JcTzbREkaQ7QLyIEQ2TYEZmISJMp
GcqRK2e6fILBX9XqWDh7o/kOr+m0IZ0pgF5zXX1puTa2hTPsBE/ZiM6hChcCGBp1
Zu+ILx+iGO77eMy/IFbyx2EUnt0zJueYSOAuK3mqlQbk2SYkJndIekOvA5i602MI
Cx3VPHsbAgMBAAECggEABc4aVX6Oj1yx1eikVG5hyhwU9rBSoaZZHwd+c1JONUhH
c5QrG8kCQg2086fNIULjTzVzT0X4h6TXtxNRqlJh52+01LLhRneg6HDofj+tk4dW
vs0Vdi3RY8sT1bcB+kll/nvGlW5zU5AwOJbCqW3oZ0fhcUd6X60oLjCAZQ3uKchM
8yfYG985zJ/OEB434LN0vlOyNan6fR3anKWY0rtx/cWLa5j5iFjN0NJFmGoYGBiu
UcYicPicYFWMGXqhDJhRsoC+aEihLzwwTzAMrwy248nuLTo6McT3V1XEIKykXb/V
1uVruD2avBF6taJlAgSR+oXqdfnqx2BChzHOzQK7+QKBgQDprFefPczAkpydMODi
Zw/pu8fTFa4xEgM6PgqSfvT4yhiWb9nfhXv7EWlfyeHLhQTmRpq+h3Ybp8E3ntP+
kKYNAHvmywEP/xfKhtaYSQUb63k4LQbSZb9XA6OxsiLvNoZEE0wzuVbY2illBeTG
lRsrdIJJ+E3sYn/1lR2rQX9LnQKBgQDWEoSoXdJrnPRjqEkxaus5oGNOQrE6CRgF
XBvHm9doEsTNRPr3Z1BzX2+IN5DpK7kgRuWYrQwm3CToNfgj+LTe8XLRWUBzxF2Y
MuN0r6mP8OUkcZBtRFH++WGNqrMEJ/2m+S9Ye35gsVysHJtnyb3aNpiFGe4+YrVD
tWoYZxhwFwKBgCyO6ZJ7BV0/V8/9rxRdFMK8RQlyW+oNhkIH7JoszWfXmcKuB3zB
BnhExLQ4We8mKV4D3qQwToxNe+GwTrp/OLrH+dhzo3s6aH39IlSdr/S3/UCCDYf3
UPo1vnQ3BMRawFWg3GoMkIv/Zd9WtV5Mtoady+5xA+LskXvx+FtcvPpdAoGAS9Vj
iQEzeUuwh10mEIt/qHpYs3CMt7JhAAUREjTyqbt8W/sDrIC8zyIPsIF+pBsJCZYT
33HtzBZQPLJhpNyFtjRyKBcl7dyyCyh7yuovdv4vLinMr+hz448UL8s4f1BrWqsL
Spz0t+wcmBvKMYoV5ydQAFafPxpYfBPX8a0TyyECgYBsPHTNv0/1DWJEUthNGD5+
7Tcq0FC5oHJ3w45RIFf7JyaYRunYR7wYLRXTIIvnez+VwGzl92hI5bRcHlQRej5Y
KmI5QJYADBKu6PISYHFr0GW1NGh9w5Q39w+rxIol7hNywNHtZg9pHv891tMQ4bSs
nKmM5vvrhTu8zJTs62mZ4A==
-----END PRIVATE KEY-----
