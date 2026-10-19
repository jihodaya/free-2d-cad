using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ACadSharp.Entities;
using ACadSharp.Objects;
using Free2DCad.Core;
using Free2DCad.Rendering;

namespace Free2DCad.App;

/// <summary>
/// 여러 도면 탭 (기존 CAD처럼): 도면마다 보기·배치·선택을 따로 기억하고 탭으로 전환한다.
/// </summary>
public sealed partial class MainWindow
{
	private sealed class DocSession
	{
		public DocSession(EditorDocument doc, string newName)
		{
			this.Doc = doc;
			this.NewName = newName;
		}

		public EditorDocument Doc { get; }

		/// <summary>저장하지 않은 새 도면의 이름 (Drawing1.dwg 등).</summary>
		public string NewName { get; }

		public Dictionary<string, ViewTransform> SpaceViews { get; } = new(StringComparer.OrdinalIgnoreCase);

		public string ActiveSpaceName { get; set; } = "Model";

		public Layout? ActiveLayout { get; set; }

		public ViewTransform? View { get; set; }

		public List<Entity> Selection { get; } = new();

		public bool Fresh { get; set; } = true;

		public string FileName => string.IsNullOrEmpty(this.Doc.FilePath) ? this.NewName : Path.GetFileName(this.Doc.FilePath);
	}

	private readonly List<DocSession> _sessions = new();
	private DocSession? _session;
	private int _drawingCounter;
	private readonly StackPanel _docTabsPanel = new() { Orientation = Orientation.Horizontal, Spacing = 1 };

	/// <summary>지금 도면의 보기·배치·선택을 기억한다 (다른 탭으로 가기 전).</summary>
	private void SaveSessionState()
	{
		if (this._session is not { } s)
		{
			return;
		}
		if (this._canvas.View != null)
		{
			this._spaceViews[this._activeSpaceName] = this._canvas.View;
			s.View = this._canvas.View;
		}
		s.SpaceViews.Clear();
		foreach (var kv in this._spaceViews) s.SpaceViews[kv.Key] = kv.Value;
		s.ActiveSpaceName = this._activeSpaceName;
		s.ActiveLayout = this._activeLayout;
		s.Selection.Clear();
		s.Selection.AddRange(this._canvas.SelectedEntities);
	}

	/// <summary>탭을 바꿀 수 있는지: 문자 편집은 확정하고, 명령은 취소. 블록 편집기 안이면 못 바꿈.</summary>
	private bool PrepareLeaveSession()
	{
		if (this._beditBlock != null)
		{
			this._cmdLine.Print("블록 편집기를 먼저 닫으십시오 (BCLOSE).");
			return false;
		}
		if (this._inplaceEditor.IsActive) this._inplaceEditor.Commit();
		if (this._canvas.ActiveCommand != null) this.OnCancelRequested();
		return true;
	}

	/// <summary>도면 탭 하나를 화면에 붙인다.</summary>
	private void ActivateSession(DocSession s)
	{
		if (this._doc != null)
		{
			this._doc.Changed -= this.OnDocumentChanged;
			this._doc.CurrentSpaceChanged -= this.OnCurrentSpaceChanged;
		}
		this._session = s;
		this._doc = s.Doc;
		this._doc.Changed += this.OnDocumentChanged;
		this._doc.CurrentSpaceChanged += this.OnCurrentSpaceChanged;
		this._spaceViews.Clear();
		foreach (var kv in s.SpaceViews) this._spaceViews[kv.Key] = kv.Value;
		this._activeSpaceName = s.ActiveSpaceName;
		this._activeLayout = s.ActiveLayout;
		this._canvas.SelectedEntities.Clear();
		this._canvas.Document = s.Doc;

		// 전에 보던 화면 그대로 (새로 그리기는 비동기라 먼저 보기를 넣고 keepView로)
		if (!s.Fresh && s.View != null)
		{
			this._canvas.View = s.View;
			this.RefreshCurrentSpaceView(keepView: true);
		}
		else
		{
			this.RefreshCurrentSpaceView(keepView: false);
		}
		s.Fresh = false;
		foreach (var e in s.Selection.Where(e => e.Owner != null)) this._canvas.SelectedEntities.Add(e);

		this._propertyPanel.AttachDocument(s.Doc, () => this._canvas.View);
		this._propertyPanel.RefreshViewProperties();
		this._propertyPanel.UpdateSelection(this._canvas.SelectedEntities);
		this._ribbon.UpdateLayers(s.Doc.Document.Layers, s.Doc.CurrentLayer);
		this._ribbon.UpdateLineTypes(s.Doc.Document.LineTypes, s.Doc.Document.Header.CurrentLineTypeName);
		this.UpdateTitleLayerCombo();
		this.RebuildLayoutTabBar();
		this.UpdateTitle();
		this.SwitchToDrawingCanvas();
	}

	/// <summary>새 탭으로 도면 추가.</summary>
	private DocSession AddSession(EditorDocument doc)
	{
		this.SaveSessionState();
		var s = new DocSession(doc, string.IsNullOrEmpty(doc.FilePath) ? $"Drawing{++this._drawingCounter}.dwg" : string.Empty);
		this._sessions.Add(s);
		this.ActivateSession(s);
		return s;
	}

	/// <summary>손대지 않은 빈 새 도면 (파일을 열면 이 탭을 대신 쓴다 — 기존 CAD와 같음).</summary>
	private static bool IsPristine(DocSession s) => string.IsNullOrEmpty(s.Doc.FilePath) && !s.Doc.IsDirty && !s.Doc.Document.Entities.Any();

	private void SwitchSession(DocSession s)
	{
		if (ReferenceEquals(s, this._session))
		{
			this.SwitchToDrawingCanvas();
			return;
		}
		if (!this.PrepareLeaveSession())
		{
			return;
		}
		this.SaveSessionState();
		this.ActivateSession(s);
		this._statusMsg.Text = s.FileName;
	}

	/// <summary>탭 닫기 (바뀐 내용이 있으면 저장 확인). 닫았으면 true.</summary>
	private async Task<bool> CloseSessionAsync(DocSession s)
	{
		if (!ReferenceEquals(s, this._session))
		{
			this.SwitchSession(s);
			if (!ReferenceEquals(s, this._session)) return false;
		}
		else if (!this.PrepareLeaveSession())
		{
			return false;
		}
		if (s.Doc.IsDirty)
		{
			var dialog = new SaveChangesDialog(s.FileName);
			await dialog.ShowDialog(this);
			switch (dialog.Choice)
			{
				case SaveChoice.Save:
					if (!await this.SaveAsync()) return false;
					break;
				case SaveChoice.DontSave:
					break;
				default:
					return false;
			}
		}
		int idx = this._sessions.IndexOf(s);
		this._sessions.Remove(s);
		this._session = null;
		if (this._sessions.Count == 0)
		{
			// 마지막 도면을 닫으면 빈 새 도면을 두고 시작 페이지로
			this.AddSession(EditorDocument.New());
			this.SwitchToStartPage();
		}
		else
		{
			this.ActivateSession(this._sessions[Math.Clamp(idx - 1, 0, this._sessions.Count - 1)]);
		}
		return true;
	}

	/// <summary>창을 닫을 때: 바뀐 도면마다 저장 확인. 모두 처리했으면 true.</summary>
	private async Task<bool> ConfirmCloseAllAsync()
	{
		foreach (var s in this._sessions.ToList())
		{
			if (!s.Doc.IsDirty) continue;
			if (!ReferenceEquals(s, this._session))
			{
				this.SwitchSession(s);
				if (!ReferenceEquals(s, this._session)) return false;
			}
			var dialog = new SaveChangesDialog(s.FileName);
			await dialog.ShowDialog(this);
			switch (dialog.Choice)
			{
				case SaveChoice.Save:
					if (!await this.SaveAsync()) return false;
					break;
				case SaveChoice.DontSave:
					break;
				default:
					return false;
			}
		}
		return true;
	}

	/// <summary>도면 탭 줄 다시 만들기 (이름 뒤 * = 저장 안 된 변경).</summary>
	private void RebuildDocTabs()
	{
		this._docTabsPanel.Children.Clear();
		foreach (var s in this._sessions)
		{
			bool active = ReferenceEquals(s, this._session) && !this._isStartPageActive;
			var name = new TextBlock
			{
				Text = s.FileName + (s.Doc.IsDirty ? "*" : string.Empty),
				FontSize = 11,
				FontWeight = active ? FontWeight.Bold : FontWeight.Normal,
				Foreground = active ? CadTheme.DocTabActiveFg : CadTheme.DocTabInactiveFg,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(6, 0),
			};
			var close = new Button
			{
				Content = "✕",
				Background = Brushes.Transparent,
				Foreground = active ? CadTheme.DocTabActiveFg : CadTheme.DocTabInactiveFg,
				Padding = new Thickness(2),
				FontSize = 10,
				[ToolTip.TipProperty] = "도면 닫기",
			};
			var session = s;
			close.Click += async (_, _) => await this.CloseSessionAsync(session);
			var tab = new Border
			{
				Background = active ? CadTheme.DocTabActiveBg : CadTheme.DocTabInactiveBg,
				BorderBrush = CadTheme.DropdownBorder,
				BorderThickness = new Thickness(1, 1, 1, 0),
				Padding = new Thickness(6, 1),
				Cursor = new Cursor(StandardCursorType.Hand),
				Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { name, close } },
				[ToolTip.TipProperty] = string.IsNullOrEmpty(s.Doc.FilePath) ? s.NewName : s.Doc.FilePath,
			};
			tab.PointerPressed += (_, e) =>
			{
				if (e.Source is Visual src && (src is Button || src.FindAncestorOfType<Button>() != null))
				{
					return; // ✕ 버튼: 여기서 탭 줄을 다시 만들면 버튼이 사라져 클릭이 안 됐다
				}
				if (e.GetCurrentPoint(tab).Properties.IsMiddleButtonPressed)
				{
					_ = this.CloseSessionAsync(session); // 가운데 버튼 = 닫기
				}
				else
				{
					this.SwitchSession(session);
				}
			};
			this._docTabsPanel.Children.Add(tab);
		}
	}
}
