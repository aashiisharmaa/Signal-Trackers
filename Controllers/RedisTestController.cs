using Microsoft.AspNetCore.Mvc;
using SignalTracker.Models;
using System;
using System.Threading.Tasks;

namespace SignalTracker.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RedisTestController : ControllerBase
    {
        private readonly RedisService _redis;

        public RedisTestController(RedisService redis)
        {
            _redis = redis;
        }

        [HttpGet("test")]
        public async Task<IActionResult> TestRedis()
        {
            try
            {
                var connected = _redis.IsConnected;

                var pingOk = await _redis.PingAsync();
                if (!pingOk)
                {
                    return StatusCode(503, new
                    {
                        success = false,
                        message = "Redis is not reachable.",
                        connected
                    });
                }

                var probeKey = $"redis-test:{Guid.NewGuid():N}";
                var probeValue = $"Redis working at {DateTime.UtcNow:O}";

                var stored = await _redis.SetStringAsync(probeKey, probeValue, 60);
                if (!stored)
                {
                    return StatusCode(503, new
                    {
                        success = false,
                        message = "Redis write test failed.",
                        connected
                    });
                }

                var value = await _redis.GetStringAsync(probeKey);
                await _redis.DeleteAsync(probeKey);

                if (value != probeValue)
                {
                    return StatusCode(503, new
                    {
                        success = false,
                        message = "Redis read-back test failed.",
                        connected
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = "Redis connected successfully.",
                    connected,
                    data = value
                });
            }
            catch (Exception ex)
            {
                return StatusCode(503, new
                {
                    success = false,
                    message = "Redis connection failed.",
                    error = ex.Message,
                    connected = _redis.IsConnected
                });
            }
        }
    }
}
