// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Channels;

public class TaskLoggingHelper
{
	internal void LogMessage(MessageImportance importance, string message)
		=> Console.WriteLine($"[{importance}] {message}");

	internal void LogWarning(string subcategory, string warningCode, string helpKeyword, string file, int lineNumber, int columnNumber, int endLineNumber, int endColumnNumber, string message, params object[] messageArgs)
		=> Console.WriteLine($"[{subcategory}] {string.Format(message, messageArgs)}");
}
