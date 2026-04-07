
using SignalTracker.Controllers;
using StackExchange.Redis;
using System.Text.Json;

namespace SignalTracker.Models
{
    public class RedisService
    {
        private readonly IConnectionMultiplexer? _multiplexer;
        private readonly IDatabase? _db;

        public RedisService(IConnectionMultiplexer? multiplexer)
        {
            _multiplexer = multiplexer;
            _db = multiplexer?.GetDatabase();
        }

        public bool IsConnected => _multiplexer?.IsConnected ?? false;

        // ---------------- BASIC ----------------

        public async Task<bool> PingAsync()
        {
            if (_db == null) return false;

            try
            {
                var pong = await _db.PingAsync();
                return pong.TotalMilliseconds >= 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Redis PingAsync error: {ex.Message}");
                return false;
            }
        }

        public async Task<T?> GetObjectAsync<T>(string key) where T : class
        {
            if (_db == null) return null;

            var value = await _db.StringGetAsync(key);
            if (value.IsNullOrEmpty) return null;

            return JsonSerializer.Deserialize<T>(value!);
        }

        public async Task<bool> SetObjectAsync<T>(string key, T value, int ttlSeconds = 300) where T : class
        {
            if (_db == null) return false;

            var json = JsonSerializer.Serialize(value);
            return await _db.StringSetAsync(key, json, TimeSpan.FromSeconds(ttlSeconds));
        }

        public async Task<string?> GetStringAsync(string key)
        {
            if (_db == null) return null;

            var value = await _db.StringGetAsync(key);
            return value.IsNullOrEmpty ? null : value.ToString();
        }

        public async Task<bool> SetStringAsync(string key, string value, int ttlSeconds = 300)
        {
            if (_db == null) return false;
            return await _db.StringSetAsync(key, value, TimeSpan.FromSeconds(ttlSeconds));
        }

        public async Task<bool> DeleteAsync(string key)
        {
            if (_db == null) return false;
            return await _db.KeyDeleteAsync(key);
        }

        // ---------------- FIX #1: GET KEYS ----------------

        public async Task<List<string>> GetKeysAsync(string pattern, int maxCount = 1000, int count = 0)
        {
            var keys = new List<string>();

            if (_multiplexer == null)
                return keys;

            try
            {
                var endpoints = _multiplexer.GetEndPoints();
                if (endpoints.Length == 0)
                    return keys;

                var server = _multiplexer.GetServer(endpoints.First());

                await foreach (var key in server.KeysAsync(pattern: pattern))
                {
                    keys.Add(key.ToString());
                    if (keys.Count >= maxCount)
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Redis GetKeysAsync error: {ex.Message}");
            }

            return keys;
        }

        // ---------------- TTL ----------------

        public async Task<TimeSpan?> GetTtlAsync(string key)
        {
            if (_db == null) return null;
            return await _db.KeyTimeToLiveAsync(key);
        }

        public async Task<bool> ExtendTtlAsync(string key, int ttlSeconds)
        {
            if (_db == null) return false;
            return await _db.KeyExpireAsync(key, TimeSpan.FromSeconds(ttlSeconds));
        }

        // ---------------- MAINTENANCE ----------------

        public async Task<bool> FlushAllAsync()
        {
            if (_multiplexer == null) return false;

            foreach (var ep in _multiplexer.GetEndPoints())
            {
                var server = _multiplexer.GetServer(ep);
                await server.FlushDatabaseAsync();
            }

            return true;
        }
        

        internal async Task<long> DeleteByPatternAsync(string v)
        {
            throw new NotImplementedException();
        }

        internal async Task<IEnumerable<object>> GetKeysAsync(object pattern, object limit)
        {
            throw new NotImplementedException();
        }

        internal async Task DeleteKeyAsync(string redisKey)
        {
            throw new NotImplementedException();
        }

        internal async Task<bool> SetObjectAsync(object cacheKey, MapViewController.NetworkLogFullResponse cacheModel, int ttlSeconds)
        {
            throw new NotImplementedException();
        }
    }
}
