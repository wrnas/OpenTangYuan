using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IConfiguration _configuration;
    //private readonly IBindingService _bindingService; // 你需要定义这个服务，或暂时注释掉

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        IConfiguration configuration) // 暂时可以注入一个空的服务，或者后面再处理
        : base(options, logger, encoder, clock)
    {
        _configuration = configuration;
        //_bindingService = bindingService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // 1. 从请求头获取 API Key
        if (!Request.Headers.TryGetValue("X-API-Key", out var apiKeyValues))
        {
            return AuthenticateResult.NoResult();
        }
        var apiKey = apiKeyValues.First();

        // 2. 验证 API Key 是否与配置一致
        var expectedApiKey = _configuration["CozeApiKey"];
        if (string.IsNullOrEmpty(expectedApiKey) || apiKey != expectedApiKey)
        {
            return AuthenticateResult.Fail("Invalid API Key");
        }

        // 3. 获取 external_userid（可以从请求头或参数获取，这里我们从参数获取）
        // 注意：在这个 Handler 里，我们可能无法读取请求体，所以建议从查询参数或头获取
        // 我们暂时假设 external_userid 从查询参数 "user_id" 获取
        // 但 Handler 中读取查询参数比较麻烦，可以简单地从请求头中取一个自定义头
        // 如果你希望从参数取，可以在控制器中再验证，Handler 只验证 API Key
        // 这里我们简化：Handler 只验证 API Key，用户 ID 在控制器里从参数获取后再验证绑定
        // 这样更灵活，也避免 Handler 里解析参数

        // 4. 构建 Claims（只放 API Key 验证通过的信息）
        var claims = new[]
        {
            new Claim("auth_type", "apikey"),
            // 可以放一个固定标识，表示 API Key 验证通过
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}