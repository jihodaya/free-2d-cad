using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Free2DCad.Core.Actions;
using Free2DCad.Core.Commands;
using Free2DCad.Rendering;
using AcColor = ACadSharp.Color;

namespace Free2DCad.App;

/// <summary>문자 바로 편집 중인 문자: Raw = 지금 서식 문자열 (문자 편집기 탭의 글자별 서식이 여기에 들어간다).</summary>
internal sealed class LiveTextSession
{
	public LiveTextSession(Entity ent, bool isM, string raw, Action redraw)
	{
		this.Entity = ent;
		this.IsMText = isM;
		this.Raw = raw;
		this.Redraw = redraw;
	}

	public Entity Entity { get; }

	public bool IsMText { get; }

	public string Raw { get; set; }

	public Action Redraw { get; }

	/// <summary>선택 없이 누른 서식 (다음에 치는 글자에 입힌다, 기존 CAD와 같음): 코드, 커서 위치.</summary>
	public (Func<MTextFormat, string> Code, int At)? Pending { get; set; }
}

/// <summary>리본의 문자 편집기 탭 (기존 CAD "텍스트 편집"): 문자를 고치는 동안만 보인다.</summary>
public sealed partial class MainWindow
{
	private LiveTextSession? _liveText;
	private bool _textDialogOpen;
	private bool _textRulerOn = true;
	private TextEditActions? _textActions;

	private void SetupTextEditTab()
	{
		this._inplaceEditor.KeepOpenFor = el => this._textDialogOpen || this._ribbon.IsTextEditUi(el);
		this._inplaceEditor.Opened += () =>
		{
			this._textActions = this.BuildTextEditActions();
			this._ribbon.ShowTextEditTab(this._textActions);
			this.UpdateTextEditState();
		};
		this._canvas.InplaceRightClick = this.OnInplaceRightClick;
		this._inplaceEditor.Closed += () =>
		{
			this._ribbon.HideTextEditTab();
			this._liveText = null;
			this._canvas.InplaceRuler = null;
		};
		this._inplaceEditor.SelectionMoved += this.UpdateTextEditState;
		this._inplaceEditor.Edited += this.OnTextEdited;
	}

	/// <summary>선택 없이 누른 서식: 커서 자리에 새로 친 글자에 입힌다.</summary>
	private void OnTextEdited(string before, string after)
	{
		if (this._liveText is not { Pending: { } p } s)
		{
			return;
		}
		int added = after.Length - before.Length;
		int caret = this._inplaceEditor.CaretIndex;
		if (added > 0 && caret - added == p.At)
		{
			string full = MTextTokenizer.ReplacePlain(s.Raw, after, true);
			s.Raw = MTextTokenizer.ApplyFormat(full, p.At, caret, p.Code);
			this._inplaceEditor.RefreshPreview();
		}
		s.Pending = null;
		this.UpdateTextEditState();
	}

	/// <summary>지금 서식 문자열 (편집 중인 글 반영).</summary>
	private string CurrentRaw(LiveTextSession s) => MTextTokenizer.ReplacePlain(s.Raw, this._inplaceEditor.CurrentText, s.IsMText);

	/// <summary>선택 글자(없으면 커서 앞 글자)의 서식으로 리본 버튼 상태 맞춤.</summary>
	private void UpdateTextEditState()
	{
		var s = this._liveText;
		if (s == null || !this._inplaceEditor.IsActive)
		{
			return;
		}
		var (a, b) = this._inplaceEditor.LastSelection;
		string raw = this.CurrentRaw(s);
		if (a == b)
		{
			int c = this._inplaceEditor.CaretIndex;
			(a, b) = c > 0 ? (c - 1, c) : (0, 1);
		}
		var fs = s.IsMText ? MTextTokenizer.FormatsOf(raw, a, b) : new List<MTextFormat>();
		var f0 = fs.FirstOrDefault() ?? MTextFormat.Default;
		bool All(Func<MTextFormat, bool> on) => fs.Count > 0 && fs.All(on);
		var (style, height, attach) = s.Entity switch
		{
			MText m => (m.Style, m.Height, (AttachmentPointType?)m.AttachmentPoint),
			MultiLeader ml => (ml.ContextData.TextStyle, ml.ContextData.TextHeight, (AttachmentPointType?)null),
			TextEntity t => (t.Style, t.Height, (AttachmentPointType?)null),
			_ => ((TextStyle?)null, 0.0, (AttachmentPointType?)null),
		};
		AcColor? color = f0.Color is { } rgb ? FindAci(rgb) : AcColor.ByLayer;
		this._ribbon.UpdateTextEditState(new TextEditState(
			All(f => f.Bold), All(f => f.Italic), All(f => f.Strike), All(f => f.Underline), All(f => f.Overline),
			f0.Font ?? (style?.Filename is { Length: > 0 } sf ? (sf.EndsWith(".shx", StringComparison.OrdinalIgnoreCase) ? sf : System.IO.Path.GetFileNameWithoutExtension(sf)) : null), color, f0.ObliqueDeg ?? 0, f0.Tracking, f0.Width,
			style?.Name, f0.Height ?? height * f0.HeightScale, attach, this._inplaceEditor.AutoUpper, this._textRulerOn));
		this.UpdateTextRuler();
	}

	private static AcColor? FindAci(Rgb rgb)
	{
		for (short i = 1; i <= 9; i++)
		{
			var c = new AcColor(i);
			var r = i == 7 ? Rgb.White : new Rgb(c.R, c.G, c.B);
			if (r.Equals(rgb)) return c;
		}
		return null;
	}

	private string StyleFontName(TextStyle? style) =>
		style?.Filename is { Length: > 0 } fn && !fn.EndsWith(".shx", StringComparison.OrdinalIgnoreCase) ? System.IO.Path.GetFileNameWithoutExtension(fn) : "맑은 고딕";

	/// <summary>여러 줄 문자 편집 상자 위의 자 (기존 CAD처럼, 옵션 → 자).</summary>
	private void UpdateTextRuler()
	{
		if (this._textRulerOn && this._liveText?.Entity is MultiLeader)
		{
			// 다중지시선: 폭 없는 문자처럼 문자 범위에 맞춘 자 (비어 있으면 기존 CAD처럼 ◁▷ 손잡이만)
			this._canvas.InplaceRuler = null;
			this._canvas.InplaceRulerAuto = true;
			return;
		}
		if (!this._textRulerOn || this._liveText?.Entity is not MText m)
		{
			this._canvas.InplaceRuler = null;
			this._canvas.InplaceRulerAuto = false;
			return;
		}
		double w = m.RectangleWidth;
		if (w <= 1e-9)
		{
			this._canvas.InplaceRuler = null;
			this._canvas.InplaceRulerAuto = true;
			return;
		}
		double ax = m.AttachmentPoint switch
		{
			AttachmentPointType.TopCenter or AttachmentPointType.MiddleCenter or AttachmentPointType.BottomCenter => 0.5,
			AttachmentPointType.TopRight or AttachmentPointType.MiddleRight or AttachmentPointType.BottomRight => 1,
			_ => 0,
		};
		double x0 = m.InsertPoint.X - w * ax;
		this._canvas.InplaceRulerAuto = false;
		this._canvas.InplaceRuler = (x0, x0 + w);
	}

	private TextEditActions BuildTextEditActions()
	{
		var ed = this._inplaceEditor;
		var doc = this._doc;
		var s = this._liveText;
		var common = new TextEditActions
		{
			Insert = t => ed.InsertAtCaret(t),
			InsertField = kind => ed.InsertAtCaret(kind switch
			{
				"date" => DateTime.Now.ToString("yyyy-MM-dd"),
				"datetime" => DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
				"filename" => this._session?.FileName ?? string.Empty,
				_ => Environment.UserName,
			}),
			Undo = ed.UndoEdit,
			Redo = ed.RedoEdit,
			Done = ed.Commit,
			FindReplace = () => _ = this.TextFindReplaceAsync(),
			ImportText = () => _ = this.ImportTextAsync(),
			AutoCaps = on => ed.AutoUpper = on,
			UpperSelection = () => this.ChangeSelectionCase(true),
			LowerSelection = () => this.ChangeSelectionCase(false),
			SpellCheck = on =>
			{
				TextSpellChecker.Enabled = on;
				ed.RecheckSpelling();
				ed.Refocus();
			},
			HighlightColor = () => _ = this.PickHighlightColorAsync(),
			Help = this.ShowHelpDialog,
		};
		if (s == null || doc == null)
		{
			return common; // 치수 입력 칸: 글자 넣기·대소문자·되돌리기만
		}
		var ent = s.Entity;
		var styles = doc.Document.TextStyles.Select(t => t.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();

		// 엔티티 값 바꾸기 (되돌리기 가능) 뒤 다시 그림
		void EditEntity(string name, Action apply, Action revert)
		{
			doc.Execute(new EntityEditAction(name, ent, apply, revert));
			s.Redraw();
			ed.RefreshPreview();
			ed.Refocus();
			this.UpdateTextEditState();
		}

		void SetStyle(string name)
		{
			if (!doc.Document.TextStyles.TryGetValue(name, out var ns)) return;
			switch (ent)
			{
				case MText m: { var old = m.Style; EditEntity("문자 스타일", () => m.Style = ns, () => m.Style = old); break; }
				case TextEntity t: { var old = t.Style; EditEntity("문자 스타일", () => t.Style = ns, () => t.Style = old); break; }
				case MultiLeader ml:
					{
						var old = ml.ContextData.TextStyle;
						EditEntity("문자 스타일", () => { ml.ContextData.TextStyle = ns; ml.TextStyle = ns; }, () => { ml.ContextData.TextStyle = old; ml.TextStyle = old; });
						break;
					}
			}
		}

		void SetEntityHeight(double h)
		{
			switch (ent)
			{
				case MText m: { double old = m.Height; EditEntity("문자 높이", () => m.Height = h, () => m.Height = old); break; }
				case TextEntity t: { double old = t.Height; EditEntity("문자 높이", () => t.Height = h, () => t.Height = old); break; }
				case MultiLeader ml:
					{
						double old = ml.ContextData.TextHeight;
						EditEntity("문자 높이",
							() => { ml.ContextData.TextHeight = h; MLeaderBuilder.SetText(ml, ml.ContextData.TextLabel ?? string.Empty); },
							() => { ml.ContextData.TextHeight = old; MLeaderBuilder.SetText(ml, ml.ContextData.TextLabel ?? string.Empty); });
						break;
					}
			}
		}

		if (!s.IsMText)
		{
			// 단일 행 문자 (TEXT): 글자별 서식은 없고 객체 전체 값 (기존 CAD도 같음)
			var t = (TextEntity)ent;
			return new TextEditActions
			{
				StyleNames = styles,
				SetStyle = SetStyle,
				SetHeight = h => { if (h > 0) SetEntityHeight(h); },
				SetColor = c => { var old = t.Color; EditEntity("문자 색", () => t.Color = c, () => t.Color = old); },
				SetOblique = d => { double old = t.ObliqueAngle; EditEntity("기울기", () => t.ObliqueAngle = d * Math.PI / 180, () => t.ObliqueAngle = old); },
				SetWidth = w => { double old = t.WidthFactor; if (w > 0) EditEntity("폭 비율", () => t.WidthFactor = w, () => t.WidthFactor = old); },
				Insert = common.Insert,
				InsertField = common.InsertField,
				UpperSelection = common.UpperSelection,
				LowerSelection = common.LowerSelection,
				Undo = common.Undo,
				Redo = common.Redo,
				Done = common.Done,
				FindReplace = common.FindReplace,
				ImportText = common.ImportText,
				AutoCaps = common.AutoCaps,
			};
		}

		// ===== 여러 줄 문자·다중지시선: 선택한 글자에 서식 =====
		var style0 = ent switch { MText m => m.Style, MultiLeader ml => ml.ContextData.TextStyle, _ => null };
		string defaultFont = this.StyleFontName(style0);
		string Font(MTextFormat f) => (f.Font ?? defaultFont).Replace(";", string.Empty);

		// 선택이 있으면 그 글자에, 없으면 다음에 칠 글자에 (기존 CAD와 같음)
		void Range(Func<IReadOnlyList<MTextFormat>, Func<MTextFormat, string>> code)
		{
			string full = this.CurrentRaw(s);
			var (a, b) = ed.LastSelection;
			if (a == b)
			{
				int c = ed.CaretIndex;
				var around = MTextTokenizer.FormatsOf(full, Math.Max(0, c - 1), Math.Max(1, c));
				s.Pending = (code(around.Count > 0 ? around : new[] { MTextFormat.Default }), c);
				ed.Refocus();
				this.UpdateTextEditState();
				return;
			}
			var formats = MTextTokenizer.FormatsOf(full, a, b);
			if (formats.Count == 0) return;
			s.Raw = MTextTokenizer.ApplyFormat(full, a, b, code(formats));
			ed.RefreshPreview();
			ed.Refocus();
			this.UpdateTextEditState();
		}

		void Toggle(Func<MTextFormat, bool> on, Func<bool, string> codeFor) => Range(fs =>
		{
			bool all = fs.All(on);
			return _ => codeFor(!all);
		});

		// 원래 문자열의 선택 자리를 새 조각으로 바꾸고 편집 글도 맞춘다 (분수·첨자처럼 보이는 글이 달라지는 경우)
		void ReplaceSelectionRaw(Func<string, string> make)
		{
			var (a, b) = ed.LastSelection;
			if (a == b) return;
			string full = this.CurrentRaw(s);
			if (MTextTokenizer.RawRange(full, a, b) is not { } r) return;
			string plainSel = ed.CurrentText.Replace("\r\n", "\n")[a..Math.Min(b, ed.CurrentText.Length)];
			s.Raw = full[..r.SrcStart] + make(plainSel) + full[r.SrcEnd..];
			string plain = MTextTokenizer.PlainText(s.Raw);
			if (plain != ed.CurrentText) ed.ReplaceText(plain);
			ed.RefreshPreview();
			ed.Refocus();
		}

		static string Esc(string t) => MTextTokenizer.Escape(t).Replace(";", "\\;");

		return new TextEditActions
		{
			StyleNames = styles,
			SetStyle = SetStyle,
			SetHeight = h =>
			{
				if (h <= 0) return;
				var (a, b) = ed.LastSelection;
				if (a == b) SetEntityHeight(h); // 선택이 없으면 전체 높이
				else Range(_ => _ => $"\\H{h:0.####};");
			},
			Bold = () => Range(fs =>
			{
				bool all = fs.All(f => f.Bold);
				return f => $"\\f{Font(f)}|b{(all ? 0 : 1)}|i{(f.Italic ? 1 : 0)};";
			}),
			Italic = () => Range(fs =>
			{
				bool all = fs.All(f => f.Italic);
				return f => $"\\f{Font(f)}|b{(f.Bold ? 1 : 0)}|i{(all ? 0 : 1)};";
			}),
			Strike = () => Toggle(f => f.Strike, on => on ? "\\K" : "\\k"),
			Underline = () => Toggle(f => f.Underline, on => on ? "\\L" : "\\l"),
			Overline = () => Toggle(f => f.Overline, on => on ? "\\O" : "\\o"),
			Superscript = () => ReplaceSelectionRaw(t => $"\\S{Esc(t)}^ ;"),
			Subscript = () => ReplaceSelectionRaw(t => $"\\S^ {Esc(t)};"),
			UpperSelection = common.UpperSelection,
			LowerSelection = common.LowerSelection,
			SetFont = name => Range(_ => f => name.EndsWith(".shx", StringComparison.OrdinalIgnoreCase)
				? $"\\F{name.Replace(";", string.Empty)};" // SHX 글꼴은 \F
				: $"\\f{name.Replace(";", string.Empty)}|b{(f.Bold ? 1 : 0)}|i{(f.Italic ? 1 : 0)};"),
			SetColor = c => Range(_ => _ => $"\\C{(c.IsByLayer ? 256 : c.IsByBlock ? 0 : c.Index)};"),
			SetOblique = d => Range(_ => _ => $"\\Q{d:0.####};"),
			SetTracking = v => Range(_ => _ => $"\\T{Math.Clamp(v, 0.75, 4):0.####};"),
			SetWidth = v => Range(_ => _ => $"\\W{Math.Max(v, 0.1):0.####};"),
			ClearCharFormat = () =>
			{
				var (a, b) = ed.LastSelection;
				if (a == b) (a, b) = (0, ed.CurrentText.Length);
				string full = this.CurrentRaw(s);
				if (MTextTokenizer.RawRange(full, a, b) is not { } r) return;
				// 선택 글자만 서식 없이 (보이는 글은 그대로)
				string plainSel = ed.CurrentText.Replace("\r\n", "\n")[a..Math.Min(b, ed.CurrentText.Length)];
				s.Raw = full[..r.SrcStart] + "{\\f" + defaultFont + "|b0|i0;\\l\\o\\k\\C256;\\Q0;\\T1;\\W1;" + MTextTokenizer.Escape(plainSel) + "}" + full[r.SrcEnd..];
				ed.RefreshPreview();
				ed.Refocus();
				this.UpdateTextEditState();
			},
			ClearParagraphFormat = () =>
			{
				s.Raw = System.Text.RegularExpressions.Regex.Replace(this.CurrentRaw(s), @"\\p[^;]*;", string.Empty);
				ed.RefreshPreview();
				ed.Refocus();
			},
			ClearAllFormat = () =>
			{
				s.Raw = MTextTokenizer.Escape(ed.CurrentText);
				ed.RefreshPreview();
				ed.Refocus();
				this.UpdateTextEditState();
			},
			CombineParagraphs = () =>
			{
				var (a, b) = ed.LastSelection;
				if (a == b) return;
				string t = ed.CurrentText.Replace("\r\n", "\n");
				b = Math.Min(b, t.Length);
				ed.ReplaceText(t[..a] + t[a..b].Replace('\n', ' ') + t[b..]);
			},
			SetAttachment = ent is MText mt0
				? ap => { var old = mt0.AttachmentPoint; EditEntity("자리맞춤", () => mt0.AttachmentPoint = ap, () => mt0.AttachmentPoint = old); }
				: null,
			SetParagraphAlign = c =>
			{
				var (a, b) = ed.LastSelection;
				if (a == b && ed.CurrentText.Length > 0 && ed.LastSelection == (0, 0)) b = 0;
				s.Raw = MTextTokenizer.SetParagraphAlign(this.CurrentRaw(s), a, b, c);
				ed.RefreshPreview();
				ed.Refocus();
			},
			SetLineSpacing = ent switch
			{
				MText m => f => { double old = m.LineSpacing; EditEntity("줄 간격", () => m.LineSpacing = f, () => m.LineSpacing = old); },
				MultiLeader ml => f => { double old = ml.ContextData.LineSpacingFactor; EditEntity("줄 간격", () => ml.ContextData.LineSpacingFactor = f, () => ml.ContextData.LineSpacingFactor = old); },
				_ => null,
			},
			SetList = kind => this.ApplyList(kind),
			Paragraph = () => _ = this.ParagraphDialogAsync(s, ent),
			SpellCheck = common.SpellCheck,
			HighlightColor = common.HighlightColor,
			Help = common.Help,
			// 문자 세트: 선택 글자 글꼴에 |c번호 (TrueType 글꼴만)
			SetCharset = cs => Range(_ => f => $"\\f{Font(f)}|b{(f.Bold ? 1 : 0)}|i{(f.Italic ? 1 : 0)}|c{cs};"),
			BackgroundMask = ent is MText maskText ? () => _ = this.BackgroundMaskAsync(maskText, EditEntity) : null,
			SetColumns = ent is MText colText ? kind => _ = this.SetColumnsAsync(colText, this.CurrentRaw(s), kind, EditEntity) : null,
			ColumnType = ent is MText colText2 ? () => MTextColumns.Read(colText2)?.Type ?? 0 : null,
			Stack = () => ReplaceSelectionRaw(t =>
			{
				// a/b 가로선 분수, a#b 사선 분수, a^b 공차 (기존 CAD 스택 텍스트)
				int k = t.IndexOfAny(new[] { '/', '#', '^' });
				return k > 0 ? $"\\S{Esc(t[..k])}{t[k]}{Esc(t[(k + 1)..])};" : Esc(t);
			}),
			Ruler = on =>
			{
				this._textRulerOn = on;
				this.UpdateTextRuler();
				this._canvas.InvalidateVisual();
				ed.Refocus();
			},
			Insert = common.Insert,
			InsertField = common.InsertField,
			Undo = common.Undo,
			Redo = common.Redo,
			Done = common.Done,
			FindReplace = common.FindReplace,
			ImportText = common.ImportText,
			AutoCaps = common.AutoCaps,
		};
	}

	/// <summary>
	/// 단락 창 (기존 CAD와 같음): 커서·선택이 걸친 문단의 들여쓰기·탭·정렬·간격을 \px...; 로 (값은 문자 높이 배수).
	/// </summary>
	private async Task ParagraphDialogAsync(LiveTextSession s, Entity ent)
	{
		var ed = this._inplaceEditor;
		var (a, b) = ed.LastSelection;
		if (a == b) a = b = ed.CaretIndex;
		string raw = this.CurrentRaw(s);
		var cur = MTextTokenizer.ParagraphAt(raw, Math.Min(a, b));
		double h = ent switch
		{
			MText m => m.Height,
			MultiLeader ml => ml.ContextData.TextHeight,
			_ => 1,
		};
		char defaultAlign = ent is MText mt
			? mt.AttachmentPoint switch
			{
				AttachmentPointType.TopCenter or AttachmentPointType.MiddleCenter or AttachmentPointType.BottomCenter => 'c',
				AttachmentPointType.TopRight or AttachmentPointType.MiddleRight or AttachmentPointType.BottomRight => 'r',
				_ => 'l',
			}
			: 'l';
		var dlg = new ParagraphDialog(cur.ToRelative(h), h);
		this._textDialogOpen = true;
		try
		{
			await dlg.ShowDialog(this);
		}
		finally
		{
			this._textDialogOpen = false;
		}
		if (dlg.Result is { } p)
		{
			s.Raw = MTextTokenizer.SetParagraph(this.CurrentRaw(s), Math.Min(a, b), Math.Max(a, b), p, defaultAlign, h);
			ed.RefreshPreview();
		}
		ed.Refocus();
	}

	/// <summary>
	/// 열 (기존 CAD "열" 메뉴·칼럼 설정): 열 없음 / 동적 자동·수동 높이 / 정적 N열 / 설정 창. 기존 CAD와 같은 확장 데이터로 저장.
	/// 열 폭은 지금 열 폭(없으면 글상자 폭), 높이는 지금 정한 높이(없으면 글 높이).
	/// </summary>
	private async Task SetColumnsAsync(MText mt, string raw, string kind, Action<string, Action, Action> edit)
	{
		var cur = MTextColumns.Read(mt);
		double width = cur?.Width ?? (mt.RectangleWidth > 0 ? mt.RectangleWidth : 50);
		double textH = cur?.DefinedHeight ?? 0;
		if (textH <= 0)
		{
			// 지금 글을 글상자 폭으로 나눈 높이 (기존 CAD: 열을 처음 켤 때 열 높이 = 지금 글 높이)
			var probe = (MText)mt.Clone();
			probe.Value = raw;
			double mh = (this._doc != null ? SceneBuilder.MeasureText(probe, this._doc.Document, keepWidth: true) : null)?.Height ?? 0;
			textH = mh > 0 ? mh : mt.Height;
		}
		double gutter = cur?.Gutter ?? Math.Round(mt.Height * 0.28, 4); // 기존 CAD 기본 (높이 2.5 → 0.7)
		MTextColumnInfo? next;
		switch (kind)
		{
			case "none":
				next = null;
				break;
			case "dynamic-auto":
				next = new MTextColumnInfo(2, true, 1, false, width, gutter, Array.Empty<double>(), textH);
				break;
			case "dynamic-manual":
				{
					int n = Math.Max(1, cur?.Columns ?? 1);
					next = new MTextColumnInfo(2, false, n, false, width, gutter, Enumerable.Repeat(textH, n).ToArray(), textH);
					break;
				}
			case "settings":
			case "settings-static":
				{
					var dlg = new ColumnSettingsDialog(cur, width, textH, gutter, kind == "settings-static");
					this._textDialogOpen = true;
					try
					{
						await dlg.ShowDialog(this);
					}
					finally
					{
						this._textDialogOpen = false;
						this._inplaceEditor.Refocus();
					}
					if (!dlg.Accepted) return;
					next = dlg.Result;
					break;
				}
			default:
				{
					if (!kind.StartsWith("static:", StringComparison.Ordinal) || !int.TryParse(kind[7..], out int n)) return;
					next = new MTextColumnInfo(1, false, n, false, width, gutter, new double[n], textH);
					break;
				}
		}
		double oldW = mt.RectangleWidth, oldH = mt.RectangleHeight;
		edit("열", () =>
		{
			MTextColumns.Write(mt, next);
			if (next == null) mt.RectangleWidth = width;
		}, () =>
		{
			MTextColumns.Write(mt, cur);
			mt.RectangleWidth = oldW;
			mt.RectangleHeight = oldH;
		});
		this._inplaceEditor.Refocus();
	}

	/// <summary>배경 마스크 (기존 CAD "배경 설정"): 90 플래그(1 채우기 색, 2 도면 배경색), 45 계수, 63 색.</summary>
	private async Task BackgroundMaskAsync(MText mt, Action<string, Action, Action> edit)
	{
		var flags = mt.BackgroundFillFlags;
		bool use = flags.HasFlag(BackgroundFillFlags.UseBackgroundFillColor) || flags.HasFlag(BackgroundFillFlags.UseDrawingWindowColor);
		var color = mt.BackgroundColor.IsByLayer || mt.BackgroundColor.IsByBlock ? new ACadSharp.Color(1) : mt.BackgroundColor;
		var dlg = new BackgroundMaskDialog(use, flags.HasFlag(BackgroundFillFlags.UseDrawingWindowColor), mt.BackgroundScale, color);
		this._textDialogOpen = true;
		try
		{
			await dlg.ShowDialog(this);
		}
		finally
		{
			this._textDialogOpen = false;
			this._inplaceEditor.Refocus();
		}
		if (!dlg.Accepted)
		{
			return;
		}
		var newFlags = !dlg.UseMask ? BackgroundFillFlags.None
			: dlg.UseDrawingBackground ? BackgroundFillFlags.UseBackgroundFillColor | BackgroundFillFlags.UseDrawingWindowColor
			: BackgroundFillFlags.UseBackgroundFillColor;
		var (oldFlags, oldScale, oldColor) = (mt.BackgroundFillFlags, mt.BackgroundScale, mt.BackgroundColor);
		var (newScale, newColor) = (dlg.Scale, dlg.Color);
		edit("배경 마스크", () =>
		{
			mt.BackgroundFillFlags = newFlags;
			mt.BackgroundScale = newScale;
			if (newFlags != BackgroundFillFlags.None) mt.BackgroundColor = newColor;
		}, () =>
		{
			mt.BackgroundFillFlags = oldFlags;
			mt.BackgroundScale = oldScale;
			mt.BackgroundColor = oldColor;
		});
	}

	/// <summary>편집기 설정 ▸ 텍스트 강조 색상: 선택 영역 색.</summary>
	private async Task PickHighlightColorAsync()
	{
		var c0 = this._canvas.InplaceSelectionColor;
		var dlg = new ColorPickerDialog(new ACadSharp.Color(c0.Red, c0.Green, c0.Blue));
		this._textDialogOpen = true;
		try
		{
			var res = await dlg.ShowDialog<ACadSharp.Color?>(this);
			if (res is { } c)
			{
				this._canvas.InplaceSelectionColor = new SkiaSharp.SKColor(c.R, c.G, c.B, 0x70);
				this._canvas.InvalidateVisual();
			}
		}
		finally
		{
			this._textDialogOpen = false;
			this._inplaceEditor.Refocus();
		}
	}

	/// <summary>
	/// 문자 편집 중 오른쪽 클릭 메뉴 (기존 CAD와 같은 순서): 맞춤법 추천·추가 제안 / 모두 선택·잘라내기·복사·붙여넣기 /
	/// 필드 삽입·기호·텍스트 가져오기 / 단락 정렬·단락·글머리 기호 및 목록·Columns / 찾기 및 바꾸기·대소문자 변경·자동 대문자 /
	/// 단락 결합·서식 지우기·배경 마스크·편집기 설정 / 도움말·취소.
	/// </summary>
	private bool OnInplaceRightClick(double wx, double wy, Avalonia.Point screen)
	{
		var ed = this._inplaceEditor;
		if (!ed.IsActive || this._textActions is not { } a)
		{
			return false;
		}
		ed.TryPlaceCaret(wx, wy);
		var items = new List<(string, Action?)>();

		// 커서 자리의 틀린 단어 → 추천 (앞 3개, 나머지는 추가 제안)
		string text = ed.CurrentText;
		int caret = ed.CaretIndex;
		var err = (this._canvas.InplaceSpellErrors ?? new List<(int Start, int Length)>())
			.FirstOrDefault(x => caret >= x.Start && caret <= x.Start + x.Length && x.Length > 0);
		if (err.Length > 0 && err.Start + err.Length <= text.Length)
		{
			string word = text.Substring(err.Start, err.Length);
			var sugg = TextSpellChecker.Suggest(word, 10);
			foreach (string w in sugg.Take(3))
			{
				string pick = w;
				items.Add((pick.Replace(">", "›").Replace("|", "¦"), () => { ed.ReplaceRange(err.Start, err.Length, pick); }));
			}
			foreach (string w in sugg.Skip(3))
			{
				string pick = w;
				items.Add(("추가 제안>" + pick.Replace(">", "›").Replace("|", "¦"), () => { ed.ReplaceRange(err.Start, err.Length, pick); }));
			}
			if (sugg.Count <= 3) items.Add(("추가 제안>(없음)", null));
			items.Add(("모두 무시", () => { TextSpellChecker.Ignore(word); ed.RecheckSpelling(); }));
			items.Add(("-", null));
		}

		items.Add(("모두 선택|Ctrl+A", ed.SelectAll));
		items.Add(("잘라내기|Ctrl+X", ed.HasSelection ? ed.Cut : null));
		items.Add(("복사|Ctrl+C", ed.HasSelection ? ed.Copy : null));
		items.Add(("붙여넣기|Ctrl+V", ed.Paste));
		items.Add(("-", null));
		if (a.InsertField != null)
		{
			items.Add(("필드 삽입>날짜", () => a.InsertField("date")));
			items.Add(("필드 삽입>날짜와 시간", () => a.InsertField("datetime")));
			items.Add(("필드 삽입>파일 이름", () => a.InsertField("filename")));
			items.Add(("필드 삽입>작성자", () => a.InsertField("author")));
		}
		if (a.Insert != null)
		{
			foreach (var (label, sym) in RibbonControl.TextSymbols)
			{
				string t = sym;
				items.Add(("기호>" + label.Replace(">", "›").Replace("|", "¦"), () => a.Insert(t)));
			}
		}
		items.Add(("텍스트 가져오기...", a.ImportText));
		items.Add(("-", null));
		if (a.SetParagraphAlign != null)
		{
			foreach (var (label, c) in new[] { ("왼쪽", 'l'), ("가운데", 'c'), ("오른쪽", 'r'), ("양쪽 맞춤", 'j'), ("균등 분할", 'd') })
			{
				char ch = c;
				items.Add(("단락 정렬>" + label, () => a.SetParagraphAlign(ch)));
			}
		}
		items.Add(("단락...", a.Paragraph));
		if (a.SetList != null)
		{
			items.Add(("글머리 기호 및 목록>끄기", () => a.SetList("off")));
			items.Add(("글머리 기호 및 목록>문자 (a. b. c.)", () => a.SetList("a")));
			items.Add(("글머리 기호 및 목록>숫자 (1. 2. 3.)", () => a.SetList("1")));
			items.Add(("글머리 기호 및 목록>글머리 기호 (•)", () => a.SetList("•")));
		}
		if (a.SetColumns is { } setCols)
		{
			items.Add(("Columns>열 없음", () => setCols("none")));
			items.Add(("Columns>동적 열 - 자동 높이", () => setCols("dynamic-auto")));
			items.Add(("Columns>동적 열 - 수동 높이", () => setCols("dynamic-manual")));
			for (int n = 2; n <= 6; n++)
			{
				string k = "static:" + n;
				items.Add(($"Columns>정적 열 {n}", () => setCols(k)));
			}
			items.Add(("Columns>열 설정...", () => setCols("settings")));
		}
		items.Add(("-", null));
		items.Add(("찾기 및 바꾸기...|Ctrl+R", a.FindReplace));
		items.Add(("대소문자 변경>대문자", a.UpperSelection));
		items.Add(("대소문자 변경>소문자", a.LowerSelection));
		items.Add(("자동 대문자", a.AutoCaps == null ? null : () => a.AutoCaps(!ed.AutoUpper)));
		items.Add(("-", null));
		items.Add(("단락 결합", a.CombineParagraphs));
		items.Add(("서식 지우기>문자 서식 지우기", a.ClearCharFormat));
		items.Add(("서식 지우기>단락 서식 지우기", a.ClearParagraphFormat));
		items.Add(("서식 지우기>모두 지우기", a.ClearAllFormat));
		items.Add(("배경 마스크...", a.BackgroundMask));
		items.Add(("편집기 설정>항상 WYSIWYG으로 표시", null));
		items.Add(("편집기 설정>텍스트 강조 색상...", a.HighlightColor));
		items.Add(("-", null));
		items.Add(("도움말|F1", a.Help));
		items.Add(("취소", () => { }));

		var menu = RibbonControl.BuildMenu(items);
		this._textDialogOpen = true;
		menu.Closed += (_, _) =>
		{
			this._textDialogOpen = false;
			ed.Refocus();
		};
		menu.Open(this._canvas);
		return true;
	}

	/// <summary>선택 글자 대·소문자 (선택이 없으면 전체).</summary>
	private void ChangeSelectionCase(bool upper)
	{
		var ed = this._inplaceEditor;
		string t = ed.CurrentText;
		var (a, b) = ed.LastSelection;
		if (a == b) (a, b) = (0, t.Length);
		b = Math.Min(b, t.Length);
		string mid = upper ? t[a..b].ToUpperInvariant() : t[a..b].ToLowerInvariant();
		ed.ReplaceText(t[..a] + mid + t[b..]);
	}

	/// <summary>목록: 선택(없으면 커서) 문단마다 앞에 번호·기호 (끄기 = 없앰).</summary>
	private void ApplyList(string kind)
	{
		var ed = this._inplaceEditor;
		var lines = ed.CurrentText.Replace("\r\n", "\n").Split('\n');
		var (a, b) = ed.LastSelection;
		int pos = 0, n = 0;
		var re = new System.Text.RegularExpressions.Regex(@"^(\d+\.|[a-z]\.|•)\t");
		for (int i = 0; i < lines.Length; i++)
		{
			int start = pos, end = pos + lines[i].Length;
			pos = end + 1;
			bool touches = a == b ? (a >= start && a <= end) : (a <= end && b > start);
			if (!touches) continue;
			string body = re.Replace(lines[i], string.Empty);
			n++;
			lines[i] = kind switch
			{
				"off" => body,
				"1" => $"{n}.\t{body}",
				"a" => $"{(char)('a' + (n - 1) % 26)}.\t{body}",
				_ => $"•\t{body}",
			};
		}
		ed.ReplaceText(string.Join("\n", lines));
	}

	/// <summary>텍스트 가져오기: .txt 파일 내용을 커서 자리에.</summary>
	private async Task ImportTextAsync()
	{
		this._textDialogOpen = true;
		try
		{
			var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
			{
				Title = "텍스트 가져오기",
				AllowMultiple = false,
				FileTypeFilter = new[] { new FilePickerFileType("텍스트 (*.txt)") { Patterns = new[] { "*.txt" } } },
			});
			string? path = files.FirstOrDefault()?.TryGetLocalPath();
			if (path != null)
			{
				string text = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
				if (text.Contains('�'))
				{
					System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
					text = System.IO.File.ReadAllText(path, System.Text.Encoding.GetEncoding(949));
				}
				this._inplaceEditor.InsertAtCaret(text.Replace("\r\n", "\n"));
			}
		}
		finally
		{
			this._textDialogOpen = false;
			this._inplaceEditor.Refocus();
		}
	}

	/// <summary>편집 중인 문자 안에서 찾아 바꾸기.</summary>
	private async Task TextFindReplaceAsync()
	{
		var find = new TextBox { Width = 260 };
		var repl = new TextBox { Width = 260 };
		var info = new TextBlock { FontSize = 12, Foreground = Avalonia.Media.Brushes.DimGray };
		var matchCase = new CheckBox { Content = "대/소문자 구분", FontSize = 12 };
		var dlg = new Window
		{
			Title = "찾기 및 바꾸기",
			Width = 380,
			SizeToContent = SizeToContent.Height,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			RequestedThemeVariant = ThemeVariant.Light,
			Background = CadTheme.DialogBg,
		};
		var all = new Button { Content = "모두 바꾸기", Width = 96, IsDefault = true };
		var close = new Button { Content = "닫기", Width = 84, IsCancel = true };
		all.Click += (_, _) =>
		{
			if (string.IsNullOrEmpty(find.Text)) return;
			string cur = this._inplaceEditor.CurrentText;
			var cmp = matchCase.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
			int n = 0;
			var sb = new System.Text.StringBuilder();
			int i = 0;
			while (true)
			{
				int k = cur.IndexOf(find.Text, i, cmp);
				if (k < 0) break;
				sb.Append(cur, i, k - i).Append(repl.Text ?? string.Empty);
				i = k + find.Text.Length;
				n++;
			}
			sb.Append(cur, i, cur.Length - i);
			if (n > 0) this._inplaceEditor.ReplaceText(sb.ToString());
			info.Text = $"{n}개를 바꿨습니다.";
		};
		close.Click += (_, _) => dlg.Close();
		dlg.Content = new StackPanel
		{
			Margin = new Thickness(12),
			Spacing = 8,
			Children =
			{
				new TextBlock { Text = "찾을 문자", FontSize = 12 }, find,
				new TextBlock { Text = "바꿀 문자", FontSize = 12 }, repl,
				matchCase,
				info,
				new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { all, close } },
			},
		};
		this._textDialogOpen = true;
		try
		{
			await dlg.ShowDialog(this);
		}
		finally
		{
			this._textDialogOpen = false;
			this._inplaceEditor.Refocus();
		}
	}
}
