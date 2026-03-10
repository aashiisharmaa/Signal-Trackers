namespace SignalTracker.Services
{
    public interface IDbConnectionProvider
    {
        string GetConnectionString();
    }

    public class DbConnectionProvider : IDbConnectionProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfiguration _configuration;
        private static readonly string[] MainDbOnlyAdminPaths = new[]
        {
            // NOTE: /admin/getusers is intentionally excluded here so that TW users
            // (country_code == "TW") correctly query the TW database for their user list.
            // Write/mutate paths still stay on Main DB to avoid dual-write issues.
            "/admin/getuser",
            "/admin/getuserbyid",
            "/admin/saveuserdetails",
            "/admin/deleteuser",
            "/admin/activateuser"
        };

        public DbConnectionProvider(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
        {
            _httpContextAccessor = httpContextAccessor;
            _configuration = configuration;
        }

        public string GetConnectionString()
        {
            var context = _httpContextAccessor.HttpContext;
            if (context == null)
                return _configuration.GetConnectionString("MySqlConnection");

            var path = (context.Request.Path.Value ?? string.Empty).ToLowerInvariant();

            // Login/auth endpoints must use the main DB.
            if (path.Contains("/home/userlogin") ||
                path.Contains("/api/auth/login") ||
                path.Contains("/home/getloggeduser"))
            {
                return _configuration.GetConnectionString("MySqlConnection");
            }

            // User management write-paths are always sourced from MainDB.
            // IMPORTANT: use exact equality (==) not StartsWith, because
            // "/admin/getusers" would otherwise match the prefix "/admin/getuser"
            // and get incorrectly pinned to Main DB.
            if (Array.Exists(MainDbOnlyAdminPaths, p => string.Equals(path, p, StringComparison.OrdinalIgnoreCase)))
            {
                return _configuration.GetConnectionString("MySqlConnection");
            }

            // Explicit override for diagnostics/manual API testing
            string? country = null;
            if (context.Request.Query.TryGetValue("country_code", out var queryCountry))
            {
                country = queryCountry.ToString();
            }
            if (string.IsNullOrWhiteSpace(country) &&
                context.Request.Headers.TryGetValue("x-country-code", out var headerCountry))
            {
                country = headerCountry.ToString();
            }

            // Normal flow from auth claim
            if (string.IsNullOrWhiteSpace(country))
            {
                country = context.User.FindFirst("country_code")?.Value;
            }

            // Some endpoints are not [Authorize], so use session fallback.
            if (string.IsNullOrWhiteSpace(country) && context.Session != null)
            {
                country = context.Session.GetString("country_code");
            }

            country = country?.Trim();

            if (string.Equals(country, "TW", StringComparison.OrdinalIgnoreCase))
            {
                return _configuration.GetConnectionString("MySqlConnection2");
            }

            return _configuration.GetConnectionString("MySqlConnection");
        }
    }
}
