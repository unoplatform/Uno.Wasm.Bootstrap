using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Build.Framework;

namespace Uno.Wasm.Bootstrap.Extensions;

internal static class TaskItemExtensions
{
	public static (string fullPath, string relativePath) GetFilePaths(this ITaskItem item, Microsoft.Build.Utilities.TaskLoggingHelper log, string currentProjectPath)
	{
		if (item.GetMetadata("RelativePath") is { } relativePath && !string.IsNullOrEmpty(relativePath))
		{
			log.LogMessage(MessageImportance.Low, $"RelativePath '{relativePath}' for full path '{item.GetMetadata("FullPath")}' (ItemSpec: {item.ItemSpec})");

			// This case is mainly for shared projects and files out of the baseSourceFile path
			return (item.GetMetadata("FullPath"), relativePath);
		}
		else if (item.GetMetadata("TargetPath") is { } targetPath && !string.IsNullOrEmpty(targetPath))
		{
			log.LogMessage(MessageImportance.Low, $"TargetPath '{targetPath}' for full path '{item.GetMetadata("FullPath")}' (ItemSpec: {item.ItemSpec})");

			// This is used for item remapping
			return (item.GetMetadata("FullPath"), targetPath);
		}
		else if (item.GetMetadata("Link") is { } link && !string.IsNullOrEmpty(link))
		{
			log.LogMessage(MessageImportance.Low, $"Link '{link}' for full path '{item.GetMetadata("FullPath")}' (ItemSpec: {item.ItemSpec})");

			// This case is mainly for shared projects and files out of the baseSourceFile path
			return (item.GetMetadata("FullPath"), link);
		}
		else if (item.GetMetadata("FullPath") is { } fullPath && File.Exists(fullPath))
		{
			log.LogMessage(MessageImportance.Low, $"FullPath '{fullPath}' (ItemSpec: {item.ItemSpec})");

			var sourceFilePath = item.ItemSpec;

			if (sourceFilePath.StartsWith(currentProjectPath))
			{
				// This is for files added explicitly through other targets (e.g. Microsoft.TypeScript.MSBuild)
				return (fullPath: fullPath, sourceFilePath.Replace(currentProjectPath + Path.DirectorySeparatorChar, ""));
			}
			else
			{
				return (fullPath, sourceFilePath);
			}
		}
		else
		{
			log.LogMessage(MessageImportance.Low, $"Without metadata '{item.GetMetadata("FullPath")}' (ItemSpec: {item.ItemSpec})");

			return (item.GetMetadata("FullPath"), item.ItemSpec);
		}
	}

}
