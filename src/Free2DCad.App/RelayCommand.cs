using System.Diagnostics;

namespace Free2DCad.App;

/// <summary>비동기 작업을 실행하는 최소 ICommand.</summary>
internal sealed class RelayCommand : System.Windows.Input.ICommand
{
	private readonly Func<Task> _action;

	public RelayCommand(Func<Task> action) => this._action = action;

	public RelayCommand(Action action) => this._action = () =>
	{
		action();
		return Task.CompletedTask;
	};

	public event EventHandler? CanExecuteChanged { add { } remove { } }

	public bool CanExecute(object? parameter) => true;

	public async void Execute(object? parameter)
	{
		try
		{
			await this._action();
		}
		catch (Exception ex)
		{
			Trace.WriteLine(ex);
		}
	}
}
