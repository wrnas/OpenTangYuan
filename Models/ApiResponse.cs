namespace TangYuan.Models
{
    public class ApiResponse<T>
    {
        public int code { get; set; }
        public string message { get; set; }
        public T data { get; set; }
    }

    public static class ResponseHelper
    {
        public static ApiResponse<T> Success<T>(T data, string message = "ok")
        {
            return new ApiResponse<T> { code = 200, message = message, data = data };
        }

        public static ApiResponse<T> Fail<T>(string message, int code = 500)
        {
            return new ApiResponse<T> { code = code, message = message, data = default };
        }
    }
}
