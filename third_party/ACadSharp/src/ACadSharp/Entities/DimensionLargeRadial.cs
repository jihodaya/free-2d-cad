using ACadSharp.Attributes;
using ACadSharp.Classes;
using ACadSharp.Tables;
using CSMath;
using CSMath.Extensions;
using System;

namespace ACadSharp.Entities;

/// <summary>
/// F2D: 꺾기 반지름 치수 (LARGE_RADIAL_DIMENSION, AcDbRadialDimensionLarge). 기존 CAD의 DIMJOGGED가 만드는 객체.
/// DefinitionPoint = 원 중심(10), ChordPoint = 원 위의 점(15), OverrideCenter = 재정의 중심(13), JogPoint = 꺾기 위치(14).
/// </summary>
[DxfName(DxfFileToken.EntityLargeRadialDimension)]
[DxfSubClass(DxfSubclassMarker.LargeRadialDimension)]
public class DimensionLargeRadial : Dimension, IDxfClassDefined
{
	/// <summary>원 위의 점 (치수선이 원과 만나는 점).</summary>
	[DxfCodeValue(15, 25, 35)]
	public XYZ ChordPoint { get; set; }

	/// <summary>재정의 중심 (치수선이 시작하는 점).</summary>
	[DxfCodeValue(13, 23, 33)]
	public XYZ OverrideCenter { get; set; }

	/// <summary>꺾기 위치.</summary>
	[DxfCodeValue(14, 24, 34)]
	public XYZ JogPoint { get; set; }

	[DxfCodeValue(40)]
	public double LeaderLength { get; set; }

	/// <summary>꺾기 각도 (DIMJOGANG, 기본 45°).</summary>
	public double JogAngle { get; set; } = Math.PI / 4;

	public override double Measurement => this.DefinitionPoint.DistanceFrom(this.ChordPoint);

	public override string ObjectName => DxfFileToken.EntityLargeRadialDimension;

	public override ObjectType ObjectType => ObjectType.UNLISTED;

	public override string SubclassMarker => DxfSubclassMarker.LargeRadialDimension;

	public DimensionLargeRadial() : base((DimensionType)9)
	{
	}

	public override void ApplyTransform(Transform transform)
	{
		base.ApplyTransform(transform);
		this.ChordPoint = transform.ApplyTransform(this.ChordPoint);
		this.OverrideCenter = transform.ApplyTransform(this.OverrideCenter);
		this.JogPoint = transform.ApplyTransform(this.JogPoint);
	}

	public override BoundingBox GetBoundingBox() => BoundingBox.FromPoints(new[] { this.ChordPoint, this.OverrideCenter, this.JogPoint, this.TextMiddlePoint });

	public DxfClass GetDxfClass()
	{
		return new DxfClass
		{
			ApplicationName = "ObjectDBX Classes",
			CppClassName = DxfSubclassMarker.LargeRadialDimension,
			DwgVersion = ACadVersion.AC1015,
			DxfName = DxfFileToken.EntityLargeRadialDimension,
			ItemClassId = 498,
			MaintenanceVersion = 0,
			ProxyFlags = ProxyFlags.EraseAllowed | ProxyFlags.DisablesProxyWarningDialog,
			WasZombie = false,
		};
	}

	/// <summary>
	/// 블록 (기존 CAD와 같은 모양): 재정의 중심 → 치수선에 평행하게 A까지, A → 꺾기점(DIMJOGANG 사선),
	/// 꺾기점 → 원 위 점(화살촉). 문자는 치수선에 정렬되어 위쪽(DIMTAD), 원 밖이면 치수선을 문자까지 연장.
	/// </summary>
	public override void UpdateBlock()
	{
		base.UpdateBlock();
		var style = this.ActiveStyle;
		double scale = style.ScaleFactor > 0 ? style.ScaleFactor : 1.0;
		double h = style.TextHeight * scale;
		double asz = style.ArrowSize * scale;
		double gap = Math.Abs(style.DimensionLineGap) * scale;
		XYZ c = this.DefinitionPoint, p = this.ChordPoint;
		double r = c.DistanceFrom(p);
		if (r < 1e-12)
		{
			return;
		}
		XY u = new XY((p.X - c.X) / r, (p.Y - c.Y) / r); // 중심 → 원 위 점
		XY n = new XY(-u.Y, u.X);
		XY At(double along, double side) => new XY(c.X + u.X * along + n.X * side, c.Y + u.Y * along + n.Y * side);

		double t = (this.JogPoint.X - c.X) * u.X + (this.JogPoint.Y - c.Y) * u.Y;
		XY jog = At(t, 0);
		double off = (this.OverrideCenter.X - c.X) * n.X + (this.OverrideCenter.Y - c.Y) * n.Y;
		double ang = this.JogAngle > 0.05 ? this.JogAngle : Math.PI / 4;
		XY a = At(t + Math.Abs(off) / Math.Tan(ang), off);

		Line Ln(XY s0, XY s1) => new Line(new XYZ(s0.X, s0.Y, 0), new XYZ(s1.X, s1.Y, 0))
		{
			Color = style.DimensionLineColor,
			LineWeight = style.DimensionLineWeight,
		};
		this._block.Entities.Add(Ln(new XY(this.OverrideCenter.X, this.OverrideCenter.Y), a));
		this._block.Entities.Add(Ln(jog, a));

		XY tm = new XY(this.TextMiddlePoint.X, this.TextMiddlePoint.Y);
		if (!this.IsTextUserDefinedLocation)
		{
			tm = new XY(p.X, p.Y);
			this.TextMiddlePoint = p;
		}
		double tAlong = (tm.X - c.X) * u.X + (tm.Y - c.Y) * u.Y;
		double end = tAlong > r + 1e-9 ? tAlong : r - asz; // 문자가 원 밖이면 치수선을 문자까지
		this._block.Entities.Add(Ln(jog, At(end, 0)));
		this._block.Entities.Add(this.dimensionArrow(p, new XYZ(u.X, u.Y, 0), style, SecondArrowBlock(style)));

		string text = this.GetMeasurementText(); // F2D: 문자 재정의(<>)는 GetMeasurementText가 처리
		double rot = Math.Atan2(u.Y, u.X);
		XY up = n;
		if (style.TextOutsideHorizontal)
		{
			rot = 0;
			up = new XY(0, 1);
		}
		else if (Math.Cos(rot) < -1e-9 || (Math.Abs(Math.Cos(rot)) <= 1e-9 && Math.Sin(rot) < 0))
		{
			rot += Math.PI; // 읽기 방향 유지
			up = new XY(-n.X, -n.Y);
		}
		double lift = gap + h / 2;
		MText mText = this.createTextEntity(new XYZ(tm.X + up.X * lift, tm.Y + up.Y * lift, 0), text);
		mText.Height = h;
		mText.AttachmentPoint = AttachmentPointType.MiddleCenter;
		mText.AlignmentPoint = new XYZ(Math.Cos(rot), Math.Sin(rot), 0);
		mText.Color = style.TextColor;
		if (style.Style != null)
		{
			mText.Style = style.Style;
		}
		this._block.Entities.Add(mText);
		this._block.Entities.Add(this.createDefinitionPoint(p));
		this._block.Entities.Add(this.createDefinitionPoint(this.OverrideCenter));
		this._block.Entities.Add(this.createDefinitionPoint(new XYZ(jog.X, jog.Y, 0)));
	}
}
