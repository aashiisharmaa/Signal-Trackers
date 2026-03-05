using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace SignalTracker.Services
{
    public class UserScopeService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public UserScopeService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        // Constants for Roles
        public const int ROLE_SUPER_ADMIN = 3;
        public const int ROLE_ADMIN = 2;
        public const int ROLE_USER = 1;

        /// <summary>
        /// Determines the target Company ID based on the user's role.
        /// </summary>
        public int GetTargetCompanyId(ClaimsPrincipal user, int? requestedCompanyId)
        {
            var countryCode = user.FindFirst("country_code")?.Value;
            bool isTwUser = string.Equals(countryCode, "TW", StringComparison.OrdinalIgnoreCase);

            // TW deployment works as a single-tenant environment in current setup:
            // use "all companies" scope to avoid excluding valid TW rows by company_id joins.
            if (isTwUser)
            {
                if (requestedCompanyId.HasValue && requestedCompanyId.Value > 0)
                    return requestedCompanyId.Value;
                return 0;
            }

            var roleClaim = user.FindFirst("UserTypeId")?.Value;
            int userRole = int.TryParse(roleClaim, out int r) ? r : 0;

            // CRITICAL: This block allows Super Admins to see everything
            if (userRole == 3) // 3 = Super Admin
            {
                // If they requested a specific ID, return it. Otherwise return 0 (All).
                if (requestedCompanyId.HasValue && requestedCompanyId.Value > 0)
                    return requestedCompanyId.Value;

                return 0; // 0 means "Show All Data"
            }

            // For everyone else, force their own Company ID
            var companyClaim = user.FindFirst("CompanyId")?.Value;
            return int.TryParse(companyClaim, out int cid) ? cid : 0;
        }
        public bool IsSuperAdmin(ClaimsPrincipal user)
        {
            var countryCode = user.FindFirst("country_code")?.Value;
            if (string.Equals(countryCode, "TW", StringComparison.OrdinalIgnoreCase))
                return true;

            var roleClaim = user.FindFirst("UserTypeId")?.Value;
            return int.TryParse(roleClaim, out int role) && role == ROLE_SUPER_ADMIN;
        }

        internal int GetCompanyId(ClaimsPrincipal user)
        {
            throw new NotImplementedException();
        }
    }
}
