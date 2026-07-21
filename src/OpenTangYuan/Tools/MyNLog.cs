using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebApi.Tools
{

    public interface INLogHelper
    {
        void LogError(Exception ex);
    }
    /// <summary>
    /// 
    /// </summary>

    public class NLogHelper : INLogHelper
    {
        //public static Logger logger { get; private set; }

        private readonly IHttpContextAccessor _httpContextAccessor;

        private readonly ILogger<NLogHelper> _logger;

        public NLogHelper(IHttpContextAccessor httpContextAccessor, ILogger<NLogHelper> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public void LogError(Exception ex)
        {
            LogMessage logMessage = new LogMessage();
            logMessage.IpAddress = _httpContextAccessor.HttpContext.Request.Host.Host;
            if (ex.InnerException != null)
                logMessage.LogInfo = ex.InnerException.Message;
            else
                logMessage.LogInfo = ex.Message;
            logMessage.StackTrace = ex.StackTrace;
            logMessage.OperationTime = DateTime.Now;
            logMessage.OperationName = "admin";
            _logger.LogError(LogFormat.ErrorFormat(logMessage));
        }
    }

    /// <summary>
    /// 日志消息
    /// </summary>
    public class LogMessage
    {
        /// <summary>
        /// IP
        /// </summary>
        public string IpAddress { get; set; }

        /// <summary>
        /// 操作人
        /// </summary>
        public string OperationName { get; set; }

        /// <summary>
        /// 操作时间
        /// </summary>
        public DateTime OperationTime { get; set; }

        /// <summary>
        /// 日志信息
        /// </summary>
        public string LogInfo { get; set; }

        /// <summary>
        /// 跟踪信息
        /// </summary>
        public string StackTrace { get; set; }

    }


    /// <summary>
    /// 格式化输出样式
    /// </summary>
    public class LogFormat
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="logMessage"></param>
        /// <returns></returns>
        public static string ErrorFormat(LogMessage logMessage)
        {
            StringBuilder strInfo = new StringBuilder();
            strInfo.Append("1. 操作时间: " + logMessage.OperationTime + " \r\n");
            strInfo.Append("2. 操作人: " + logMessage.OperationName + " \r\n");
            strInfo.Append("3. Ip  : " + logMessage.IpAddress + "\r\n");
            strInfo.Append("4. 错误内容: " + logMessage.LogInfo + "\r\n");
            strInfo.Append("5. 跟踪: " + logMessage.StackTrace + "\r\n");
            strInfo.Append("-----------------------------------------------------------------------------------------------------------------------------\r\n");
            return strInfo.ToString();
        }
    }
}
