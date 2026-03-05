using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Threading.Tasks;

namespace SignalTracker.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RedisTestController : ControllerBase
    {
        private readonly IDistributedCache _cache;

        public RedisTestController(IDistributedCache cache)
        {
            _cache = cache;
        }

        [HttpGet("test")]
        public async Task<IActionResult> TestRedis()
        {
            try
            {
                // Test key-value
                await _cache.SetStringAsync("redis-test-key", "Redis working!", 
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
                    });

                var value = await _cache.GetStringAsync("redis-test-key");

                if (value == null)
                    return Ok(new { success = false, message = "Redis NOT connected." });

                return Ok(new { success = true, message = "Redis connected successfully!", data = value });
            }
            catch (Exception ex)
            {
                return Ok(new { success = false, message = "Redis connection failed.", error = ex.Message });
            }
        }
    }
}
