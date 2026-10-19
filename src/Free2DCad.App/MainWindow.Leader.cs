using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Free2DCad.Core.Tools;
using Free2DCad.Core.Commands;

namespace Free2DCad.App;

/// <summary>LEADER: 공차(T) 옵션의 대화상자, 블록 찾아보기(BROWSE) — 명령 도중 여는 창.</summary>
public partial class MainWindow
{
	private void HookLeaderCommand(ClassicLeaderCommand cmd)
	{
		cmd.ToleranceRequested += c => Dispatcher.UIThread.Post(async () =>
		{
			string? text = await this.AskToleranceAsync();
			c.SetTolerance(text);
			this.AfterAsyncCommandStep(c);
		});
		cmd.BlockBrowseRequested += c => Dispatcher.UIThread.Post(async () =>
		{
			if (this._doc is not { } doc) return;
			var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "삽입할 도면 선택", AllowMultiple = false, FileTypeFilter = new[] { DwgOnly } });
			if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
			{
				try
				{
					c.SetBlock(BlockLibrary.Import(doc, path).Block);
				}
				catch (Exception ex)
				{
					this._cmdLine.Print($"파일을 읽을 수 없습니다: {ex.Message}");
				}
			}
			this.AfterAsyncCommandStep(c);
		});
	}

	/// <summary>대화상자처럼 명령줄 밖에서 명령을 진행한 뒤: 끝났으면 마무리, 아니면 다음 프롬프트.</summary>
	private void AfterAsyncCommandStep(EditorCommand cmd)
	{
		if (!ReferenceEquals(this._canvas.ActiveCommand, cmd))
		{
			return;
		}
		if (cmd.IsFinished)
		{
			this._canvas.ActiveCommand = null;
			this._canvas.ClearTracking();
			this._cmdLine.SetPrompt("명령어:");
			this.OnCommandCompleted(cmd);
		}
		else
		{
			this._cmdLine.SetPrompt(cmd.Prompt);
		}
		this._canvas.InvalidateVisual();
	}
}
