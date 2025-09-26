// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using Crucible.Common.Authentication.Claims;

namespace Crucible.Common.Tests;

public class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void WhenHasClaimsAtPath_RetrievesExpectedClaimValues()
    {
        // given a ClaimsPrincipal with a claim at realm_access.roles with value type JSON
        var identity = new ClaimsIdentity
        ([
            new Claim("realm_access", """{"roles":["role-1","role-2"]}""", "JSON")
        ]);
        var principal = new ClaimsPrincipal(identity);

        // when we ask for the an array of values at that path
        var claimValues = principal.GetClaimValues("realm_access.roles");

        // then we should get two values, and they should be what we expect
        Assert.Equal(2, claimValues.Length);
        Assert.Contains("role-1", claimValues);
        Assert.Contains("role-2", claimValues);
    }

    [Fact]
    public void WhenNoClaimAtPath_ReturnsEmpty()
    {
        // given a ClaimsPrincipal with no claims
        var principal = new ClaimsPrincipal(new ClaimsIdentity([]));

        // when we ask for roles at some path
        var claimValues = principal.GetClaimValues("a-path");

        // then we should not see an exception
        Assert.Empty(claimValues);
    }
}
