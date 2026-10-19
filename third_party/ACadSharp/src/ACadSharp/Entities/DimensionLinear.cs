using ACadSharp.Attributes;
using ACadSharp.Tables;
using CSMath;
using CSMath.Extensions;
using CSMath.Geometry;
using System;

namespace ACadSharp.Entities;

/// <summary>
/// Represents a <see cref="DimensionLinear"/> entity.
/// </summary>
/// <remarks>
/// Object name <see cref="DxfFileToken.EntityDimension"/> <br/>
/// Dxf class name <see cref="DxfSubclassMarker.LinearDimension"/>
/// </remarks>
[DxfName(DxfFileToken.EntityDimension)]
[DxfSubClass(DxfSubclassMarker.LinearDimension)]
public class DimensionLinear : DimensionAligned
{
	/// <inheritdoc/>
	public override double Measurement
	{
		get
		{
			var angle = new XYZ(System.Math.Cos(this.Rotation), System.Math.Sin(this.Rotation), 0.0);
			double dot = Math.Abs(angle.Dot((this.SecondPoint - this.FirstPoint).Normalize()));
			return base.Measurement * dot;
		}
	}

	/// <inheritdoc/>
	public override string ObjectName => DxfFileToken.EntityDimension;

	/// <inheritdoc/>
	public override ObjectType ObjectType => ObjectType.DIMENSION_LINEAR;

	/// <inheritdoc/>
	public override double Offset
	{
		get
		{
			return base.Offset;
		}
		set
		{
			var transform = Transform.CreateRotation(this.Normal, this.Rotation);
			XYZ axisY = transform.ApplyTransform(XYZ.AxisY).Normalize();

			this.DefinitionPoint = this.SecondPoint + axisY * value;
		}
	}

	/// <summary>
	/// Angle of rotated, horizontal, or vertical dimensions.
	/// </summary>
	/// <value>
	/// Value in radians.
	/// </value>
	[DxfCodeValue(DxfReferenceType.IsAngle, 50)]
	public double Rotation { get; set; }

	/// <inheritdoc/>
	public override string SubclassMarker => DxfSubclassMarker.LinearDimension;

	/// <inheritdoc/>
	protected override XYZ measurementOrigin() => this.FirstPoint;

	/// <inheritdoc/>
	public DimensionLinear() : base(DimensionType.Linear)
	{
	}

	/// <inheritdoc/>
	public override void UpdateBlock()
	{
		this.createBlock(); //Should use base, needs to be optimized

		var transform = Transform.CreateRotation(this.Normal, this.Rotation);
		XYZ yVec = transform.ApplyTransform(XYZ.AxisY).Normalize();
		XYZ xVec = transform.ApplyTransform(XYZ.AxisX).Normalize();

		// F2D: 첫 점을 치수선에 투영 (Line3D.FindIntersection은 회전 치수 등에서 NaN을 돌려 치수선이 사라졌다)
		var dimRef1 = this.DefinitionPoint + xVec * (this.FirstPoint - this.DefinitionPoint).Dot(xVec);
		var dimRef2 = this.DefinitionPoint;
		XYZ dir = (dimRef2 - dimRef1).Normalize();

		// reference points
		this._block.Entities.Add(new Point(this.FirstPoint) { Layer = Layer.Defpoints });
		this._block.Entities.Add(new Point(this.SecondPoint) { Layer = Layer.Defpoints });
		this._block.Entities.Add(new Point(dimRef1) { Layer = Layer.Defpoints });
		this._block.Entities.Add(new Point(dimRef2) { Layer = Layer.Defpoints });


		// extension lines
		var dirRef1 = (dimRef1 - this.FirstPoint).Normalize();
		var dirRef2 = (dimRef2 - this.SecondPoint).Normalize();
		double dimexo = this.ActiveStyle.ExtensionLineOffset * this.ActiveStyle.ScaleFactor;
		double dimexe = this.ActiveStyle.ExtensionLineExtension * this.ActiveStyle.ScaleFactor;
		if (!this.ActiveStyle.SuppressFirstExtensionLine)
		{
			{
				XYZ extStart = this.FirstPoint + dimexo * dirRef1;
				XYZ extEnd = dimRef1 + dimexe * dirRef1;
				// F2D: 측정점이 치수선 위에 있으면 방향이 0 벡터라 NaN 선이 생겼다 → 건너뜀
				if (isFiniteLine(extStart, extEnd))
				{
					this._block.Entities.Add(extensionLine(extStart, extEnd, this.ActiveStyle, this.ActiveStyle.LineTypeExt1));
				}
			}
		}

		if (!this.ActiveStyle.SuppressSecondExtensionLine)
		{
			{
				XYZ extStart = this.SecondPoint + dimexo * dirRef2;
				XYZ extEnd = dimRef2 + dimexe * dirRef2;
				// F2D: 측정점이 치수선 위에 있으면 방향이 0 벡터라 NaN 선이 생겼다 → 건너뜀
				if (isFiniteLine(extStart, extEnd))
				{
					this._block.Entities.Add(extensionLine(extStart, extEnd, this.ActiveStyle, this.ActiveStyle.LineTypeExt2));
				}
			}
		}

		// F2D: 치수선·화살촉·문자 배치 (맞춤·문자 위치 규칙, Dimension.layoutLinearDimension)
		this.layoutLinearDimension(dimRef1, dimRef2, dir);
	}
}