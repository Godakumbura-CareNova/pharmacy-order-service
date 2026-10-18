using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PharmacyOrder.Api.Security;

namespace PharmacyOrder.Api.Tests.Unit;

public class PharmacyStaffAuthorizationTests
{
    [Fact]
    public async Task PharmacyStaffPolicy_AllowsThePharmacyStaffRole()
    {
        var services = new ServiceCollection();
        services.AddPharmacyStaffAuthorization();

        using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await policyProvider.GetPolicyAsync(AuthorizationPolicies.PharmacyStaff);

        Assert.NotNull(policy);
        Assert.Contains(
            policy.Requirements,
            requirement => requirement is RolesAuthorizationRequirement roles
                && roles.AllowedRoles.Contains("PharmacyStaff"));
    }
}
