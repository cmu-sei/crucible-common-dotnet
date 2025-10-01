// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Text.RegularExpressions;

namespace Crucible.Common.Utilities.Services;

public interface ISlugService
{
    public string Get(string input);
}

internal partial class SlugService : ISlugService
{
    public string Get(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        // to lower
        var output = input.ToLowerInvariant();

        // replace anything not alphanumeric or dash with "-"
        output = NotSlugLegalRegex().Replace(output, "-");

        // deal with multiple consecutive dashes (because of dashes in the input, say)
        output = ConsecutiveDashesRegex().Replace(output, "-");

        // trim on the way out
        return output.Trim('-');
    }

    [GeneratedRegex("-{2,}")]
    private static partial Regex ConsecutiveDashesRegex();
    [GeneratedRegex("[^a-zA-Z0-9]+")]
    private static partial Regex NotSlugLegalRegex();
}
