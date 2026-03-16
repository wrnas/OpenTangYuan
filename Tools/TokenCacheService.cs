using System.Collections.Concurrent;

namespace WebApi.Tools
{
    public class TokenCacheService
    {
        // 内部线程安全字典：key = userId, value = refreshToken
        private readonly ConcurrentDictionary<string, string> _refreshTokenStore = new();

        public void SetRefreshToken(string userId, string refreshToken)
        {
            _refreshTokenStore[userId] = refreshToken;
        }

        public string? GetRefreshToken(string userId)
        {
            _refreshTokenStore.TryGetValue(userId, out var token);
            return token;
        }

        public bool ValidateRefreshToken(string userId, string refreshToken)
        {
            return GetRefreshToken(userId) == refreshToken;
        }

        public void RemoveRefreshToken(string userId)
        {
            _refreshTokenStore.TryRemove(userId, out _);
        }
    }
}
