using System;

namespace Pathoschild.ModData.Common;

/// <summary>Provides utility methods for logging output to the console.</summary>
public static class ConsoleHelper
{
	/// <summary>Log a message to the console window with a timestamp.</summary>
	/// <param name="message">The message to log.</param>
	public static void WriteLine(string message)
	{
		Console.WriteLine($"{DateTime.Now:HH:mm:ss} {message}");
	}

	/// <summary>Log a warning message to the console window with a timestamp.</summary>
	/// <param name="message">The message to log.</param>
	public static void WriteWarningLine(string message)
	{
		ConsoleColor wasColor = Console.ForegroundColor;

		try
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			ConsoleHelper.WriteLine(message);
		}
		finally
		{
			Console.ForegroundColor = wasColor;
		}
	}
}
