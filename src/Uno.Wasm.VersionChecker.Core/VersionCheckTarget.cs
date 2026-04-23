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
				error = $"Unsupported target '{SanitizeForDisplay(input)}'. Only http(s) URLs are allowed.";
				return false;
			}

			siteUri = absolute;
		}
		else if (candidate.Contains("://", StringComparison.Ordinal))
		{
			error = $"Unable to parse target '{SanitizeForDisplay(input)}'.";
			return false;
		}
		else if (Uri.TryCreate($"https://{candidate}", UriKind.Absolute, out var assumedHttps)
			&& !string.IsNullOrWhiteSpace(assumedHttps.Host))
		{
			siteUri = assumedHttps;
		}
		else
		{
			error = $"Unable to parse target '{SanitizeForDisplay(input)}'.";
			return false;
		}

		try
		{
			var sanitizedBuilder = new UriBuilder(siteUri)
			{
				UserName = string.Empty,
				Password = string.Empty
			};
			siteUri = sanitizedBuilder.Uri;

			if (!siteUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
			{
				siteUri = new UriBuilder(siteUri) { Path = $"{siteUri.AbsolutePath.TrimEnd('/')}/" }.Uri;
			}

			if (!VersionCheckNetworkPolicy.IsSafePublicTarget(siteUri, out error))
			{
				return false;
			}
		}
		catch (UriFormatException)
		{
			error = $"Unable to parse target '{SanitizeForDisplay(input)}'.";
			return false;
		}

		target = new VersionCheckTarget(candidate, siteUri);
		return true;
	}

	public static string SanitizeForDisplay(string? input)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return string.Empty;
		}

		if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri))
		{
			return input.Trim();
		}

		var builder = new UriBuilder(uri)
		{
			UserName = string.Empty,
			Password = string.Empty
		};
		return builder.Uri.ToString();
	}
}
