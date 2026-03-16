using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TangYuan.Models;

namespace TangYuan.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AliOSSController : ControllerBase
    {
        private readonly AliyunOssOptions _ossOptions;

        public AliOSSController(IOptions<AliyunOssOptions> ossOptions)
        {
            _ossOptions = ossOptions.Value;
        }

        /// <summary>
        /// 生成阿里云直传所需 policy
        /// </summary>
        [HttpGet("policy")]
        public IActionResult GetPolicy()
        {
            var dir = $"uploads/{DateTime.Now:yyyyMMdd}/";
            var expireEnd = DateTime.UtcNow.AddMinutes(10);
            var expiration = expireEnd.ToString("yyyy-MM-ddTHH:mm:ssZ");

            // ======= System.Text.Json.Nodes 版本的 JSON 构造 =======
            var policyNode = new JsonObject
            {
                ["expiration"] = expiration,
                ["conditions"] = new JsonArray
                {
                    new JsonArray("content-length-range", 0, 1_048_576_000),
                    new JsonArray("starts-with", "$key", dir)
                }
            };

            var policyJson = policyNode.ToJsonString();
            var policyBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(policyJson));
            var signature = ComputeSignature(policyBase64, _ossOptions.AccessKeySecret);

            var result = (new
            {
                accessKeyId = _ossOptions.AccessKeyId,
                policy = policyBase64,
                signature,
                dir,
                host = $"https://{_ossOptions.BucketName}.{_ossOptions.Endpoint}",
                expire = ((DateTimeOffset)expireEnd).ToUnixTimeSeconds()
            });           

            return Ok(ResponseHelper.Success(result, "上传完成"));
        }

        private static string ComputeSignature(string policyBase64, string accessKeySecret)
        {
            using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(accessKeySecret));

            return Convert.ToBase64String(hmac
                .ComputeHash(Encoding.UTF8.GetBytes(policyBase64)));
        }

        // ======= POCO 替代 JObject 作为输入模型 =======
        public record SaveFileRequest(string? FileUrl);

        /// <summary>
        /// 保存上传完成后的文件记录
        /// </summary>
        [HttpPost("save")]
        public IActionResult SaveFile([FromBody] SaveFileRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.FileUrl))
                return BadRequest("Missing fileUrl");

            //// 记录到数据库或日志
            //Console.WriteLine("保存文件记录：" + req.FileUrl);
            return Ok(ResponseHelper.Success("文件记录保存成功"));            
        }
    }
}
