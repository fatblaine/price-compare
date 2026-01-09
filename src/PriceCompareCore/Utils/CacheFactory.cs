using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using System;

namespace PriceCompareCore.Utils
{
    public static class CacheFactory
    {
        public static IDistributedCache Create()
        {
            var useRedis = Environment.GetEnvironmentVariable("USE_REDIS")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

            if (useRedis)
            {
                var redisConn = Environment.GetEnvironmentVariable("Redis__ConnectionString")
                    ?? "localhost:6379";

                Console.WriteLine($"[Cache] Using Redis: {redisConn}");

                return new RedisCache(new RedisCacheOptions
                {
                    Configuration = redisConn
                });
            }
            else
            {
                Console.WriteLine("[Cache] Using in-memory cache (no Redis)");
                return new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            }
        }
    }
}
