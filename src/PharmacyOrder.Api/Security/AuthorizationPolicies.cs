using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace PharmacyOrder.Api.Security;

public static class AuthorizationPolicies
{
    public const string PharmacyStaff = "PharmacyStaff";

    public static IServiceCollection AddPharmacyStaffAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                PharmacyStaff,
                policy => policy.RequireRole(PharmacyStaff));
        });

        return services;
    }
}
