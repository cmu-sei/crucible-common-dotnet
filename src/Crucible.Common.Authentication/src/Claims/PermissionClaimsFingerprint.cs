// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Cryptography;
using System.Text;

namespace Crucible.Common.Authentication.Claims;

/// <summary>
/// Builds the fingerprint an <see cref="IPermissionClaimsCache"/> entry is keyed by: a hash of the token claim values
/// an app's permission loader reads, such as the roles and groups from the identity provider.
/// </summary>
/// <remarks>
/// Pass one set per input, always in the same order, and an empty or null set for an input that is switched off, so
/// a role and a group with the same name cannot collide. Within a set the order and duplicates of values do not
/// matter. Never include the token id (<c>jti</c>) or anything else that changes on every token.
/// </remarks>
public static class PermissionClaimsFingerprint
{
    /// <summary>The fingerprint of no token inputs, for an app whose permissions come from the database alone.</summary>
    public static string None { get; } = Create();

    /// <summary>A case-insensitive fingerprint of the value sets, for loaders that match names without regard to case.</summary>
    public static string Create(params IEnumerable<string>?[] valueSets) => Create(ignoreCase: true, valueSets);

    /// <summary>A fingerprint of the value sets.</summary>
    /// <param name="ignoreCase">Whether values differing only in case are the same, as they are to the loader.</param>
    /// <param name="valueSets">One set of claim values per token input the loader reads.</param>
    public static string Create(bool ignoreCase, params IEnumerable<string>?[] valueSets)
    {
        ArgumentNullException.ThrowIfNull(valueSets);
        var builder = new StringBuilder();

        foreach (var set in valueSets)
        {
            var values = (set ?? [])
                .Where(x => x is not null)
                .Select(x => ignoreCase ? x.ToLowerInvariant() : x)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            // length-prefixed, so no choice of values can make two different inputs read the same
            builder.Append(values.Length).Append(';');

            foreach (var value in values)
            {
                builder.Append(value.Length).Append(':').Append(value);
            }

            builder.Append('|');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
