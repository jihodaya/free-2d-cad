using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ACadSharp.Tables;
using ACadSharp.Types.Units;
using AcadColor = ACadSharp.Color;
using AvColor = Avalonia.Media.Color;
using Point = Avalonia.Point;

namespace Free2DCad.App;

/// <summary>
/// 기존 CAD 치수 스타일 미리보기 컨트롤 (검은 바탕에 견본 형상과 치수들을 스타일 속성에 맞춰 그린다).
/// </summary>
public sealed class DimStylePreview : Control
{
	private DimensionStyle? _style;

	public DimStylePreview()
	{
		this.ClipToBounds = true;
	}

	public void UpdateStyle(DimensionStyle? style)
	{
		this._style = style;
		this.InvalidateVisual();
	}

	public override void Render(DrawingContext dc)
	{
		base.Render(dc);

		double w = this.Bounds.Width;
		double h = this.Bounds.Height;
		if (w <= 10 || h <= 10) return;

		// 1. 검은 배경
		dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));

		// 스타일 속성 추출
		var style = this._style;
		double dimScale = style?.ScaleFactor > 0 ? style.ScaleFactor : 1.0;
		double lfac = style?.LinearScaleFactor != 0 ? (style?.LinearScaleFactor ?? 1.0) : 1.0;
		int decPlaces = Math.Clamp((int)(style?.DecimalPlaces ?? 2), 0, 8);
		char decSep = style?.DecimalSeparator != '\0' ? style!.DecimalSeparator : '.';
		ZeroHandling zeroHand = style?.ZeroHandling ?? ZeroHandling.SuppressDecimalTrailingZeroes;
		string prefix = style?.Prefix ?? string.Empty;
		string suffix = style?.Suffix ?? string.Empty;

		// 색상
		var dimColor = ToAvaloniaColor(style?.DimensionLineColor, AvColor.FromRgb(0, 255, 0));
		var extColor = ToAvaloniaColor(style?.ExtensionLineColor, AvColor.FromRgb(0, 255, 0));
		var textColor = ToAvaloniaColor(style?.TextColor, AvColor.FromRgb(255, 255, 0));
		var geomColor = AvColor.FromRgb(0x33, 0x77, 0xFF);

		var dimPen = new Pen(new SolidColorBrush(dimColor), 1.0);
		var extPen = new Pen(new SolidColorBrush(extColor), 1.0);
		var geomPen = new Pen(new SolidColorBrush(geomColor), 1.4);
		var textBrush = new SolidColorBrush(textColor);
		var dimBrush = new SolidColorBrush(dimColor);

		// 좌표 변환 설정
		// 모델 영역: X: 40~240, Y: 20~220 (너비 ~200, 높이 ~200)
		double mMinX = 30, mMaxX = 245;
		double mMinY = 15, mMaxY = 225;
		double mW = mMaxX - mMinX;
		double mH = mMaxY - mMinY;

		double scale = Math.Min((w - 20) / mW, (h - 20) / mH);
		double offX = (w - mW * scale) / 2.0 - mMinX * scale;
		double offY = (h - mH * scale) / 2.0 - mMinY * scale;

		Point ToScreen(double mx, double my)
		{
			return new Point(offX + mx * scale, h - (offY + my * scale));
		}

		// 치수선/화살표 크기 & 텍스트 폰트 크기
		double arrowBase = style?.ArrowSize > 0 ? style.ArrowSize : 3.5;
		double arrowPx = Math.Clamp(arrowBase * dimScale * scale * 0.8, 5.0, 14.0);

		double textHeight = style?.TextHeight > 0 ? style.TextHeight : 3.5;
		double fontPx = Math.Clamp(textHeight * dimScale * scale * 0.8, 9.0, 15.0);

		string arrowType = style?.ArrowBlock?.Name ?? "Default";

		// 2. 파란색 형상 (Polygon)
		// 형상 좌표 정의
		// 상단 수평선 (길이 80.8)
		double p1x = 75, p1y = 185;
		double p2x = p1x + 80.8, p2y = p1y;
		// 우측 단차 수직 내림 (길이 37.3)
		double p3x = p2x, p3y = p2y - 37.3;
		// 우측 수평 확장
		double p4x = p3x + 38.0, p4y = p3y;
		// 우측 경사면 하단 (길이 109.2)
		double p5x = p4x + 22.0, p5y = 52;
		// 하단 수평면 (길이 160.2의 일부)
		double p6x = 150, p6y = 52;
		// 좌측 경사면 (각도 121.5)
		double p7x = 75, p7y = 185 - 74.5; // (75, 110.5)

		var polyGeom = new StreamGeometry();
		using (var sgc = polyGeom.Open())
		{
			sgc.BeginFigure(ToScreen(p1x, p1y), false);
			sgc.LineTo(ToScreen(p2x, p2y));
			sgc.LineTo(ToScreen(p3x, p3y));
			sgc.LineTo(ToScreen(p4x, p4y));
			sgc.LineTo(ToScreen(p5x, p5y));
			sgc.LineTo(ToScreen(p6x, p6y));
			sgc.LineTo(ToScreen(p7x, p7y));
			sgc.LineTo(ToScreen(p1x, p1y));
			sgc.EndFigure(true);
		}
		dc.DrawGeometry(null, geomPen, polyGeom);

		// 원 (중심 140, 118, 지름 55.9 -> 반지름 27.95)
		double cx = 138, cy = 118;
		double cr = 27.95;
		var cScreen = ToScreen(cx, cy);
		double rScreen = cr * scale;
		dc.DrawEllipse(null, geomPen, cScreen, rScreen, rScreen);

		// 센터 마크 (기존 CAD 녹색 +)
		double cmSize = (style?.CenterMarkSize ?? 1.0) * scale * 4.0;
		if (cmSize > 2)
		{
			var cmPen = new Pen(dimBrush, 1.0);
			dc.DrawLine(cmPen, new Point(cScreen.X - cmSize, cScreen.Y), new Point(cScreen.X + cmSize, cScreen.Y));
			dc.DrawLine(cmPen, new Point(cScreen.X, cScreen.Y - cmSize), new Point(cScreen.X, cScreen.Y + cmSize));
		}

		// 값 포맷팅 도우미
		string FormatVal(double v, bool isAngle = false)
		{
			if (isAngle)
			{
				int aPrec = style?.AngularDecimalPlaces ?? 1;
				if (aPrec < 0) aPrec = decPlaces;
				return v.ToString($"F{Math.Clamp(aPrec, 0, 4)}", CultureInfo.InvariantCulture) + "°";
			}
			double scaled = v * lfac;
			string s = scaled.ToString($"F{decPlaces}", CultureInfo.InvariantCulture);
			if (decSep != '.') s = s.Replace('.', decSep);
			if (zeroHand.HasFlag(ZeroHandling.SuppressDecimalTrailingZeroes) && s.Contains(decSep))
			{
				s = s.TrimEnd('0').TrimEnd(decSep);
			}
			return $"{prefix}{s}{suffix}";
		}

		// 텍스트 그리기 도우미
		void DrawDimText(string text, Point pt, double angleDeg = 0)
		{
			var ft = new FormattedText(
				text,
				CultureInfo.CurrentCulture,
				FlowDirection.LeftToRight,
				new Typeface("Segoe UI", FontStyle.Normal, FontWeight.SemiBold),
				fontPx,
				textBrush
			);

			if (Math.Abs(angleDeg) < 0.1)
			{
				dc.DrawText(ft, new Point(pt.X - ft.Width / 2.0, pt.Y - ft.Height / 2.0));
			}
			else
			{
				var state = dc.PushTransform(Matrix.CreateTranslation(-pt.X, -pt.Y) *
											 Matrix.CreateRotation(angleDeg * Math.PI / 180.0) *
											 Matrix.CreateTranslation(pt.X, pt.Y));
				dc.DrawText(ft, new Point(pt.X - ft.Width / 2.0, pt.Y - ft.Height / 2.0));
				state.Dispose();
			}
		}

		// 화살표 그리기 도우미
		void DrawArrow(Point tip, Vector dir)
		{
			if (dir.Length <= 0.001) return;
			var udir = Unit(dir);
			var unorm = new Vector(-udir.Y, udir.X);

			if (arrowType.Contains("Tick", StringComparison.OrdinalIgnoreCase) ||
				arrowType.Contains("사선", StringComparison.OrdinalIgnoreCase) ||
				arrowType.Contains("Oblique", StringComparison.OrdinalIgnoreCase))
			{
				var t1 = tip + (udir + unorm) * (arrowPx * 0.7);
				var t2 = tip - (udir + unorm) * (arrowPx * 0.7);
				dc.DrawLine(new Pen(dimBrush, 1.8), t1, t2);
				return;
			}
			if (arrowType.Contains("Dot", StringComparison.OrdinalIgnoreCase) ||
				arrowType.Contains("점", StringComparison.OrdinalIgnoreCase))
			{
				var dotC = tip + udir * (arrowPx * 0.35);
				dc.DrawEllipse(dimBrush, null, dotC, arrowPx * 0.35, arrowPx * 0.35);
				return;
			}
			if (arrowType.Contains("None", StringComparison.OrdinalIgnoreCase) ||
				arrowType.Contains("없음", StringComparison.OrdinalIgnoreCase))
			{
				return;
			}

			// 기본: Closed Filled (채움 삼각 화살표)
			var basePt = tip + udir * arrowPx;
			var side1 = basePt + unorm * (arrowPx * 0.32);
			var side2 = basePt - unorm * (arrowPx * 0.32);

			var arrowGeom = new StreamGeometry();
			using (var ac = arrowGeom.Open())
			{
				ac.BeginFigure(tip, true);
				ac.LineTo(side1);
				ac.LineTo(side2);
				ac.EndFigure(true);
			}
			dc.DrawGeometry(dimBrush, dimPen, arrowGeom);
		}

		// 3. 치수들 그리기
		bool suppDim1 = style?.SuppressFirstDimensionLine ?? false;
		bool suppDim2 = style?.SuppressSecondDimensionLine ?? false;
		bool suppExt1 = style?.SuppressFirstExtensionLine ?? false;
		bool suppExt2 = style?.SuppressSecondExtensionLine ?? false;

		// 1) 상단 수평 선형 치수 (80.8)
		{
			double dimY = p1y + 18;
			var sExt1Start = ToScreen(p1x, p1y + 2);
			var sExt1End = ToScreen(p1x, dimY + 4);
			var sExt2Start = ToScreen(p2x, p2y + 2);
			var sExt2End = ToScreen(p2x, dimY + 4);

			if (!suppExt1) dc.DrawLine(extPen, sExt1Start, sExt1End);
			if (!suppExt2) dc.DrawLine(extPen, sExt2Start, sExt2End);

			var sDim1 = ToScreen(p1x, dimY);
			var sDim2 = ToScreen(p2x, dimY);
			var sMid = new Point((sDim1.X + sDim2.X) / 2.0, sDim1.Y);

			if (!suppDim1 || !suppDim2)
			{
				dc.DrawLine(dimPen, sDim1, sDim2);
			}
			if (!suppDim1) DrawArrow(sDim1, new Vector(1, 0));
			if (!suppDim2) DrawArrow(sDim2, new Vector(-1, 0));

			DrawDimText(FormatVal(80.8), new Point(sMid.X, sMid.Y - fontPx * 0.8));
		}

		// 2) 좌측 수직 선형 치수 (74.5)
		{
			double dimX = p1x - 18;
			var sExt1Start = ToScreen(p1x - 2, p7y);
			var sExt1End = ToScreen(dimX - 4, p7y);
			var sExt2Start = ToScreen(p1x - 2, p1y);
			var sExt2End = ToScreen(dimX - 4, p1y);

			if (!suppExt1) dc.DrawLine(extPen, sExt1Start, sExt1End);
			if (!suppExt2) dc.DrawLine(extPen, sExt2Start, sExt2End);

			var sDim1 = ToScreen(dimX, p7y);
			var sDim2 = ToScreen(dimX, p1y);
			var sMid = new Point(sDim1.X, (sDim1.Y + sDim2.Y) / 2.0);

			dc.DrawLine(dimPen, sDim1, sDim2);
			DrawArrow(sDim1, new Vector(0, -1));
			DrawArrow(sDim2, new Vector(0, 1));

			bool horiz = style?.TextOutsideHorizontal ?? false;
			DrawDimText(FormatVal(74.5), new Point(sMid.X - (horiz ? 18 : 12), sMid.Y), horiz ? 0 : -90);
		}

		// 3) 우측 단차 수직 선형 치수 (37.3)
		{
			double dimX = p3x + 14;
			var sExt1Start = ToScreen(p2x + 2, p3y);
			var sExt1End = ToScreen(dimX + 4, p3y);
			var sExt2Start = ToScreen(p2x + 2, p2y);
			var sExt2End = ToScreen(dimX + 4, p2y);

			if (!suppExt1) dc.DrawLine(extPen, sExt1Start, sExt1End);
			if (!suppExt2) dc.DrawLine(extPen, sExt2Start, sExt2End);

			var sDim1 = ToScreen(dimX, p3y);
			var sDim2 = ToScreen(dimX, p2y);
			var sMid = new Point(sDim1.X, (sDim1.Y + sDim2.Y) / 2.0);

			dc.DrawLine(dimPen, sDim1, sDim2);
			DrawArrow(sDim1, new Vector(0, -1));
			DrawArrow(sDim2, new Vector(0, 1));

			DrawDimText(FormatVal(37.3), new Point(sMid.X - 14, sMid.Y), -90);
		}

		// 4) 우측 경사면 정렬 치수 (109.2)
		{
			// 경사면 (p4x, p4y) -> (p5x, p5y)
			var sP4 = ToScreen(p4x, p4y);
			var sP5 = ToScreen(p5x, p5y);
			var edgeV = sP5 - sP4;
			var edgeNorm = Unit(new Vector(-edgeV.Y, edgeV.X));

			double offsetDist = 18.0;
			var sD1 = sP4 + edgeNorm * offsetDist;
			var sD2 = sP5 + edgeNorm * offsetDist;

			dc.DrawLine(extPen, sP4, sD1 + edgeNorm * 4);
			dc.DrawLine(extPen, sP5, sD2 + edgeNorm * 4);

			dc.DrawLine(dimPen, sD1, sD2);
			DrawArrow(sD1, Unit(sD2 - sD1));
			DrawArrow(sD2, Unit(sD1 - sD2));

			var sMid = new Point((sD1.X + sD2.X) / 2.0, (sD1.Y + sD2.Y) / 2.0);
			double edgeAngle = Math.Atan2(edgeV.Y, edgeV.X) * 180.0 / Math.PI;
			if (edgeAngle > 90 || edgeAngle < -90) edgeAngle += 180;
			DrawDimText(FormatVal(109.2), sMid + edgeNorm * (fontPx * 0.8), edgeAngle);
		}

		// 5) 하단 수평 치수 (160.2)
		{
			double dimY = 32;
			var sExt1Start = ToScreen(p7x, 52);
			var sExt1End = ToScreen(p7x, dimY - 4);
			var sExt2Start = ToScreen(p5x - 30, 52);
			var sExt2End = ToScreen(p5x - 30, dimY - 4);

			dc.DrawLine(extPen, sExt1Start, sExt1End);
			dc.DrawLine(extPen, sExt2Start, sExt2End);

			var sDim1 = ToScreen(p7x, dimY);
			var sDim2 = ToScreen(p5x - 30, dimY);
			var sMid = new Point((sDim1.X + sDim2.X) / 2.0, sDim1.Y);

			dc.DrawLine(dimPen, sDim1, sDim2);
			DrawArrow(sDim1, new Vector(1, 0));
			DrawArrow(sDim2, new Vector(-1, 0));

			DrawDimText(FormatVal(160.2), new Point(sMid.X - 10, sMid.Y + fontPx * 0.8));
		}

		// 6) 각도 치수들 (121.5°, 42.7°)
		{
			// 좌하단 각도 121.5° 호
			var arcPt = ToScreen(105, 52);
			var arcGeom = new StreamGeometry();
			using (var ac = arcGeom.Open())
			{
				ac.BeginFigure(new Point(arcPt.X + 22, arcPt.Y), false);
				ac.ArcTo(new Point(arcPt.X - 10, arcPt.Y - 18), new Size(24, 24), 0, false, SweepDirection.CounterClockwise);
				ac.EndFigure(false);
			}
			dc.DrawGeometry(null, dimPen, arcGeom);
			DrawArrow(new Point(arcPt.X - 10, arcPt.Y - 18), Unit(new Vector(1, 1)));
			DrawDimText(FormatVal(121.5, true), new Point(arcPt.X + 35, arcPt.Y + 2));

			// 우하단 각도 42.7° 호
			var arc2Pt = ToScreen(p5x - 10, 52);
			var arc2Geom = new StreamGeometry();
			using (var ac2 = arc2Geom.Open())
			{
				ac2.BeginFigure(new Point(arc2Pt.X - 22, arc2Pt.Y), false);
				ac2.ArcTo(new Point(arc2Pt.X - 8, arc2Pt.Y - 16), new Size(20, 20), 0, false, SweepDirection.Clockwise);
				ac2.EndFigure(false);
			}
			dc.DrawGeometry(null, dimPen, arc2Geom);
			DrawArrow(new Point(arc2Pt.X - 8, arc2Pt.Y - 16), Unit(new Vector(-1, 1)));
			DrawDimText(FormatVal(42.7, true), new Point(arc2Pt.X + 20, arc2Pt.Y + 6));
		}

		// 7) 지름 및 반지름 치수 (∅55.9, R27.9)
		{
			// 지름: 중심 통과 대각선 (남서 -> 북동)
			double radAngle = 225.0 * Math.PI / 180.0;
			var rimPt1 = new Point(cScreen.X + rScreen * Math.Cos(radAngle), cScreen.Y - rScreen * Math.Sin(radAngle));
			var leadPt1 = rimPt1 + new Vector(Math.Cos(radAngle), -Math.Sin(radAngle)) * 16.0;
			var horizPt1 = new Point(leadPt1.X - 18, leadPt1.Y);

			dc.DrawLine(dimPen, rimPt1, leadPt1);
			dc.DrawLine(dimPen, leadPt1, horizPt1);
			DrawArrow(rimPt1, Unit(rimPt1 - cScreen));
			DrawDimText("∅" + FormatVal(55.9), new Point(horizPt1.X - 20, horizPt1.Y));

			// 반지름: 중심에서 남동 방향
			double radAngle2 = 315.0 * Math.PI / 180.0;
			var rimPt2 = new Point(cScreen.X + rScreen * Math.Cos(radAngle2), cScreen.Y - rScreen * Math.Sin(radAngle2));
			var leadPt2 = rimPt2 + new Vector(Math.Cos(radAngle2), -Math.Sin(radAngle2)) * 14.0;
			var horizPt2 = new Point(leadPt2.X + 18, leadPt2.Y);

			dc.DrawLine(dimPen, cScreen, rimPt2);
			dc.DrawLine(dimPen, rimPt2, leadPt2);
			dc.DrawLine(dimPen, leadPt2, horizPt2);
			DrawArrow(rimPt2, Unit(cScreen - rimPt2));
			DrawDimText("R" + FormatVal(27.9), new Point(horizPt2.X + 18, horizPt2.Y));
		}
	}

	private static AvColor ToAvaloniaColor(AcadColor? acadColor, AvColor defaultColor)
	{
		if (!acadColor.HasValue || acadColor.Value.IsByLayer || acadColor.Value.IsByBlock)
		{
			return defaultColor;
		}
		var rgb = acadColor.Value.GetRgb();
		return AvColor.FromRgb(rgb[0], rgb[1], rgb[2]);
	}

	/// <summary>길이 1로 맞춘 방향 (0 벡터면 그대로).</summary>
	private static Vector Unit(Vector v)
	{
		double len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
		return len > 1e-12 ? new Vector(v.X / len, v.Y / len) : v;
	}
}
