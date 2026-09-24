using System.Collections.Generic;
using Spectre.Console;

namespace Pathoschild.ModData.Common;

/// <summary>Manages updates to a set of progress bars shown by <see cref="ConsoleHelper.RunWithProgressBarsAsync"/>.</summary>
/// <remarks>This is thread-safe. If the console isn't interactive, this does nothing.</remarks>
public class ConsoleProgress
{
	/*********
	** Fields
	*********/
	/// <summary>The progress bars by label, if any.</summary>
	private readonly IReadOnlyDictionary<string, ProgressTask>? Tasks;


	/*********
	** Public methods
	*********/
	/// <summary>Construct an instance.</summary>
	/// <param name="tasks"><inheritdoc cref="Tasks" path="/summary"/></param>
	internal ConsoleProgress(IReadOnlyDictionary<string, ProgressTask>? tasks)
	{
		this.Tasks = tasks;
	}

	/// <summary>Set the number of steps for a progress bar, and mark it started.</summary>
	/// <param name="label">The progress bar label.</param>
	/// <param name="total">The number of steps to complete.</param>
	public void Start(string label, int total)
	{
		if (this.Tasks is null || !this.Tasks.TryGetValue(label, out ProgressTask? task))
			return;

		task.MaxValue = total;
		task.StartTask();
	}

	/// <summary>Advance a progress bar by one step.</summary>
	/// <param name="label">The progress bar label.</param>
	public void Increment(string label)
	{
		if (this.Tasks is null || !this.Tasks.TryGetValue(label, out ProgressTask? task))
			return;

		task.Increment(1);
	}
}
