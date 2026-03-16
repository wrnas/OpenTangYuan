using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace WebApi.Tools

{
    public static class JWTHelper
    {
        public static string GenerateAccessToken(string username, string userId, string roles, IConfiguration config)
        {
            try
            {
                var claims = new[]
            {
                new Claim(ClaimTypes.Name, username),
                new Claim("userId", userId),
                new Claim("role", roles ?? string.Empty)
            };

                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Secret"]));
                var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                // 从配置中读取过期分钟数，默认 30 分钟
                int expireMinutes = 30;
                if (!int.TryParse(config["Jwt:AccessTokenExpireMinutes"], out expireMinutes))
                {
                    expireMinutes = 30;
                }

                var token = new JwtSecurityToken(
                    issuer: config["Jwt:Issuer"],
                    audience: config["Jwt:Audience"],
                    claims: claims,
                    expires: DateTime.UtcNow.AddMinutes(expireMinutes),
                    signingCredentials: creds
                );

                return new JwtSecurityTokenHandler().WriteToken(token);
            }
            catch (Exception exp)
            {
                return string.Empty;
               // throw;
            }
        }

        public static ClaimsPrincipal? ValidateToken(string token, IConfiguration config)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(config["Jwt:Secret"]);

            try
            {
                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidIssuer = config["Jwt:Issuer"],
                    ValidAudience = config["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateLifetime = true
                }, out SecurityToken validatedToken);

                return principal;
            }
            catch
            {
                return null;
            }
        }
    }
}
