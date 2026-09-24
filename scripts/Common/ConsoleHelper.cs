using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Pathoschild.ModData.Common;

/// <summary>Provides utility methods for logging output to the console.</summary>
/// <remarks>This is thread-safe, except when mixed with concurrent direct <see cref="Console"/> writes.</remarks>
public static class ConsoleHelper
{
	/*********
	** Fields
	*********/
	/// <summary>The console which writes to the standard output stream.</summary>
	private static readonly IAnsiConsole Output = CreateConsole();

	/// <summary>The text style for warning messages.</summary>
	private static readonly Style WarningStyle = new(Color.Yellow);


	/*********
	** Public methods
	*********/
	/// <summary>Log an empty line to the console window.</summary>
	public static void WriteLine()
	{
		Output.WriteLine();
	}

	/// <summary>Log a message to the console window with a timestamp.</summary>
	/// <param name="message">The message to log.</param>
	public static void WriteLine(string message)
	{
		WriteLine(message, Style.Plain);
	}

	/// <summary>Log a warning message to the console window with a timestamp.</summary>
	/// <param name="message">The message to log.</param>
	public static void WriteWarningLine(string message)
	{
		WriteLine(message, WarningStyle);
	}

	/// <summary>Show live progress bars in the console while an action is running.</summary>
	/// <param name="labels">The progress bar labels, in display order. Each bar is shown as waiting until the action calls <see cref="ConsoleProgress.Start"/> for it.</param>
	/// <param name="run">The action to run, given an instance to update the progress bars.</param>
	/// <remarks>Messages logged through this helper while the action runs are shown above the progress bars. If the console isn't interactive (e.g. its output is redirected), the action runs without progress bars.</remarks>
	public static async Task RunWithProgressBarsAsync(IEnumerable<string> labels, Func<ConsoleProgress, Task> run)
	{
		// run without progress
		if (!Output.Profile.Capabilities.Interactive)
		{
			await run(new ConsoleProgress(null));
			return;
		}

		// run with progress bars
		await Output
			.Progress()
			.AutoClear(false)
			.Columns(
				new TaskDescriptionColumn(),
				new ProgressBarColumn(),
				new PercentageColumn(),
				new StepCountColumn(),
				new SpinnerColumn()
			)
			.StartAsync(async context =>
			{
				Dictionary<string, ProgressTask> tasks = labels.ToDictionary(
					label => label,
					label => context.AddTask(Markup.Escape(label), autoStart: false)
				);

				await run(new ConsoleProgress(tasks));
			});
	}


	/*********
	** Private methods
	*********/
	/// <summary>Create a console which writes to the standard output stream.</summary>
	private static IAnsiConsole CreateConsole()
	{
		IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
		{
			Out = new AnsiConsoleOutput(Console.Out)
		});

		if (!console.Profile.Capabilities.Interactive)
			console.Profile.Width = int.MaxValue; // disable hard word wrap when the output is redirected (e.g. to a file)

		return console;
	}

	/// <summary>Log a message to the console window with a timestamp.</summary>
	/// <param name="message">The message to log.</param>
	/// <param name="style">The text style for the message.</param>
	private static void WriteLine(string message, Style style)
	{
		Output.Write(
			new Paragraph()
				.Append($"{DateTime.Now:HH:mm:ss} ")
				.Append(message, style)
				.Append(Environment.NewLine)
		);
	}

	/// <summary>A progress column which shows the number of completed and total steps (like <c>15 / 250</c>).</summary>
	private class StepCountColumn : ProgressColumn
	{
		/// <inheritdoc />
		public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
		{
			return task.IsStarted
				? new Text($"{task.Value:N0} / {task.MaxValue:N0}")
				: Text.Empty;
		}
	}
}
