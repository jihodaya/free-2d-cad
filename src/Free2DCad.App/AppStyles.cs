using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Free2DCad.App;

/// <summary>
/// 앱 전체 공통 스타일. Fluent 어두운 테마는 마우스를 올리거나 포커스가 가면 입력 칸·콤보의 바탕을
/// 테마색(어두운 회색)으로 바꾸고 글자는 흰색으로 바꾼다. 흰 바탕 + 검은 글자로 만든 칸은 이때 글자가 사라졌다.
/// 여기서는 "컨트롤에 지정한 바탕·글자색은 그대로 두고, 파란 테두리로만 표시"하도록 전부 덮어쓴다.
/// </summary>
internal static class AppStyles
{
	private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD7));

	private static TemplateBinding Parent(Avalonia.AvaloniaProperty p) => new(p);

	public static void Apply(Styles styles)
	{
		// 탭 머리글: Fluent 기본(약 24px 글자, 높이 48)은 CAD 대화상자에 너무 크다 → 기존 CAD처럼 작은 탭
		styles.Add(new Style(x => x.OfType<TabItem>())
		{
			Setters =
			{
				new Setter(TemplatedControl.FontSizeProperty, 12.0),
				new Setter(Layoutable.MinHeightProperty, 26.0),
				new Setter(TemplatedControl.PaddingProperty, new Avalonia.Thickness(10, 2)),
			},
		});

		// 작은 CAD 컨트롤: Fluent 기본 안쪽 여백(위아래 5~7px)은 높이 22~24 칸에서 글자 아래가 잘린다.
		// 로컬 값이 스타일보다 우선이므로 Padding을 직접 준 컨트롤(리본 버튼 등)은 영향 없음
		styles.Add(new Style(x => x.OfType<Button>())
		{
			Setters =
			{
				new Setter(TemplatedControl.PaddingProperty, new Avalonia.Thickness(8, 1)),
				new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
				new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
			},
		});
		styles.Add(new Style(x => x.OfType<ComboBox>())
		{
			Setters =
			{
				new Setter(TemplatedControl.PaddingProperty, new Avalonia.Thickness(6, 0, 0, 0)),
				new Setter(ComboBox.VerticalContentAlignmentProperty, VerticalAlignment.Center),
				// Fluent 기본 최소 폭(64px) 때문에 폭을 50px 등으로 준 칸이 옆 칸·글자와 겹쳤다 → 준 폭 그대로
				new Setter(Layoutable.MinWidthProperty, 0.0),
			},
		});
		styles.Add(new Style(x => x.OfType<TextBox>())
		{
			Setters =
			{
				new Setter(TemplatedControl.PaddingProperty, new Avalonia.Thickness(4, 1)),
				new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center),
				new Setter(Layoutable.MinWidthProperty, 0.0),
			},
		});

		// 메뉴 표시줄의 펼친 메뉴: 글자를 밝은 흰색으로 (어두운 바탕에서 회색 글자가 잘 안 보였다)
		styles.Add(new Style(x => x.OfType<Menu>().Descendant().OfType<MenuItem>())
		{
			Setters = { new Setter(TemplatedControl.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5))) },
		});

		// 검은 글자 콤보(리본·속성 팔레트의 흰 칸)의 펼친 목록: 어두운 테마 바탕에 검은 글자가 묻혀 안 보였다.
		// 기존 CAD처럼 흰 목록 + 검은 글자 + 연한 파랑 선택/올림
		var lightPopup = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
		var hoverBg = new SolidColorBrush(Color.FromRgb(0xE5, 0xF1, 0xFB));
		var selBg = new SolidColorBrush(Color.FromRgb(0xCC, 0xE4, 0xF7));
		styles.Add(new Style(x => x.OfType<ComboBox>().PropertyEquals(TemplatedControl.ForegroundProperty, Brushes.Black).Template().OfType<Border>().Name("PopupBorder"))
		{
			Setters =
			{
				new Setter(Border.BackgroundProperty, lightPopup),
				new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x7A))),
			},
		});
		styles.Add(new Style(x => x.OfType<ComboBox>().PropertyEquals(TemplatedControl.ForegroundProperty, Brushes.Black).Descendant().OfType<ComboBoxItem>())
		{
			Setters =
			{
				new Setter(TemplatedControl.ForegroundProperty, Brushes.Black),
				new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
			},
		});
		foreach (var (state, bg) in new[] { (":pointerover", (IBrush)hoverBg), (":selected", selBg), (":pressed", selBg) })
		{
			styles.Add(new Style(x => x.OfType<ComboBox>().PropertyEquals(TemplatedControl.ForegroundProperty, Brushes.Black).Descendant().OfType<ComboBoxItem>().Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
			{
				Setters =
				{
					new Setter(ContentPresenter.BackgroundProperty, bg),
					new Setter(ContentPresenter.ForegroundProperty, Brushes.Black),
				},
			});
		}

		// 콤보 상자: 올림/누름에도 자기 바탕 유지 + 파란 테두리
		foreach (string state in new[] { ":pointerover", ":pressed", ":focus-within" })
		{
			styles.Add(new Style(x => x.OfType<ComboBox>().Class(state).Template().OfType<Border>().Name("Background"))
			{
				Setters =
				{
					new Setter(Border.BackgroundProperty, Parent(TemplatedControl.BackgroundProperty)),
					new Setter(Border.BorderBrushProperty, Accent),
				},
			});
			styles.Add(new Style(x => x.OfType<ComboBox>().Class(state).Template().OfType<ContentPresenter>().Name("ContentPresenter"))
			{
				Setters = { new Setter(ContentPresenter.ForegroundProperty, Parent(TemplatedControl.ForegroundProperty)) },
			});
		}

		// 입력 칸: 올림/포커스에도 자기 바탕 유지 + 파란 테두리
		foreach (string state in new[] { ":pointerover", ":focus" })
		{
			styles.Add(new Style(x => x.OfType<TextBox>().Class(state).Template().OfType<Border>().Name("PART_BorderElement"))
			{
				Setters =
				{
					new Setter(Border.BackgroundProperty, Parent(TemplatedControl.BackgroundProperty)),
					new Setter(Border.BorderBrushProperty, Accent),
				},
			});
		}

		// 버튼·목록 항목: 올림/누름에 글자색을 테마 흰색으로 바꾸지 않고 지정한 색 유지
		foreach (string state in new[] { ":pointerover", ":pressed" })
		{
			styles.Add(new Style(x => x.OfType<Button>().Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
			{
				Setters = { new Setter(ContentPresenter.ForegroundProperty, Parent(TemplatedControl.ForegroundProperty)) },
			});
			styles.Add(new Style(x => x.OfType<ListBoxItem>().Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
			{
				Setters = { new Setter(ContentPresenter.ForegroundProperty, Parent(TemplatedControl.ForegroundProperty)) },
			});
			styles.Add(new Style(x => x.OfType<ComboBoxItem>().Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
			{
				Setters = { new Setter(ContentPresenter.ForegroundProperty, Parent(TemplatedControl.ForegroundProperty)) },
			});
		}
	}
}
