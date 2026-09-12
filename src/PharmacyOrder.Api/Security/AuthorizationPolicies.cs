using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace PharmacyOrder.Api.Security;

public static class AuthorizationPolicies
{
    public const string PharmacyStaff = "PharmacyStaff";
    public const string Patient = "Patient";

    public static IServiceCollection AddPharmacyStaffAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                PharmacyStaff,
                policy => policy.RequireRole(PharmacyStaff));
            options.AddPolicy(
                Patient,
                policy => policy.RequireRole(Patient));
        });

        return services;
    }
}
