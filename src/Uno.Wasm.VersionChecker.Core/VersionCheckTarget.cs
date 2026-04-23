using System;

namespace Uno.VersionChecker;

public sealed record VersionCheckTarget(string Input, Uri SiteUri)
{
	public static bool TryParse(string? input, out VersionCheckTarget? target)
		=> TryParse(input, out target, out _);

	public static bool TryParse(string? input, out VersionCheckTarget? target, out string? error)
	{
		target = null;
		error = null;

		if (string.IsNullOrWhiteSpace(input))
		{
			error = "A target URL or hostname is required.";
			return false;
		}

		var candidate = input.Trim();
		Uri siteUri;

		if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute))
		{
			if ((absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
				|| string.IsNullOrWhiteSpace(absolute.Host))
			{
				error = $"Unsupported target '{input}'. Only http(s) URLs are allowed.";
				return false;
			}

			siteUri = absolute;
		}
		else if (candidate.Contains("://", StringComparison.Ordinal))
		{
			error = $"Unable to parse target '{input}'.";
			return false;
		}
		else if (Uri.TryCreate($"https://{candidate}", UriKind.Absolute, out var assumedHttps)
			&& !string.IsNullOrWhiteSpace(assumedHttps.Host))
		{
			siteUri = assumedHttps;
		}
		else
		{
			error = $"Unable to parse target '{input}'.";
			return false;
		}

		try
		{
			if (!siteUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
			{
				siteUri = new UriBuilder(siteUri) { Path = $"{siteUri.AbsolutePath.TrimEnd('/')}/" }.Uri;
			}
		}
		catch (UriFormatException)
		{
			error = $"Unable to parse target '{input}'.";
			return false;
		}

		target = new VersionCheckTarget(candidate, siteUri);
		return true;
	}
}
