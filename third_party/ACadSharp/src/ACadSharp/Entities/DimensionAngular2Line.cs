using System;
using ACadSharp.Attributes;
using CSMath;
using CSMath.Extensions;
using CSMath.Geometry;

namespace ACadSharp.Entities
{
	/// <summary>
	/// Represents a <see cref="DimensionAngular2Line"/> entity.
	/// </summary>
	/// <remarks>
	/// Object name <see cref="DxfFileToken.EntityDimension"/> <br/>
	/// Dxf class name <see cref="DxfSubclassMarker.Angular2LineDimension"/>
	/// </remarks>
	[DxfName(DxfFileToken.EntityDimension)]
	[DxfSubClass(DxfSubclassMarker.Angular2LineDimension)]
	public class DimensionAngular2Line : Dimension
	{
		/// <summary>
		/// Definition point for diameter, radius, and angular dimensions (in WCS).
		/// </summary>
		[DxfCodeValue(15, 25, 35)]
		public XYZ AngleVertex { get; set; }

		/// <summary>
		/// Gets the center point of the measured arc.
		/// </summary>
		public XYZ Center
		{
			get
			{
				// F2D: 두 무한 직선의 평면 교점 (Line3D.FindIntersection은 끝점이 맞닿는 경우 등에서 NaN을 돌려 치수가 그려지지 않았다)
				XYZ a = this.FirstPoint, b = this.SecondPoint, c = this.AngleVertex, d = this.DefinitionPoint;
				double rx = b.X - a.X, ry = b.Y - a.Y, sx = d.X - c.X, sy = d.Y - c.Y;
				double den = rx * sy - ry * sx;
				double scale = (Math.Abs(rx) + Math.Abs(ry)) * (Math.Abs(sx) + Math.Abs(sy));
				if (Math.Abs(den) <= 1e-12 * Math.Max(scale, 1e-300))
				{
					return new XYZ(double.NaN, double.NaN, double.NaN);
				}
				double t = ((c.X - a.X) * sy - (c.Y - a.Y) * sx) / den;
				return new XYZ(a.X + rx * t, a.Y + ry * t, a.Z);
			}
		}

		/// <summary>
		/// F2D: 호 점(DimensionArc)을 품는 부채꼴의 두 반직선 방향 (교점에서 바깥쪽, 단위 벡터).
		/// 두 선이 만드는 네 부채꼴 중 하나 — 치수 값과 블록이 같은 부채꼴을 쓴다.
		/// </summary>
		internal (XYZ D1, XYZ D2)? Sector()
		{
			XYZ c = this.Center;
			if (double.IsNaN(c.X))
			{
				return null;
			}
			XYZ r1 = (this.SecondPoint - this.FirstPoint).Normalize();
			XYZ r2 = (this.DefinitionPoint - this.AngleVertex).Normalize();
			XYZ va = this.DimensionArc - c;
			XYZ n = this.Normal;
			if (va.IsZero())
			{
				return (r1, r2);
			}
			foreach (int s1 in new[] { 1, -1 })
			{
				foreach (int s2 in new[] { 1, -1 })
				{
					XYZ a = s1 * r1, b = s2 * r2;
					double cab = XYZ.Cross(a, b).Dot(n);
					double cav = XYZ.Cross(a, va).Dot(n), cvb = XYZ.Cross(va, b).Dot(n);
					if (cab > 0 && cav >= 0 && cvb >= 0 || cab < 0 && cav <= 0 && cvb <= 0)
					{
						return (a, b);
					}
				}
			}
			return (r1, r2);
		}

		/// <summary>
		/// Point defining dimension arc for angular dimensions (in OCS).
		/// </summary>
		[DxfCodeValue(16, 26, 36)]
		public XYZ DimensionArc { get; set; }

		/// <summary>
		/// Definition point for linear and angular dimensions (in WCS).
		/// </summary>
		[DxfCodeValue(13, 23, 33)]
		public XYZ FirstPoint { get; set; }

		/// <inheritdoc/>
		public override double Measurement
		{
			get
			{
				// F2D: 호가 놓인 부채꼴의 각도 (기존 CAD와 같음)
				if (this.Sector() is { } sec)
				{
					return sec.D1.AngleBetweenVectors(sec.D2);
				}
				var v1 = this.SecondPoint - this.FirstPoint;
				var v2 = this.DefinitionPoint - this.AngleVertex;
				return v1.AngleBetweenVectors(v2);
			}
		}

		/// <inheritdoc/>
		public override string ObjectName => DxfFileToken.EntityDimension;

		/// <inheritdoc/>
		public override ObjectType ObjectType => ObjectType.DIMENSION_ANG_2_Ln;

		/// <summary>
		/// Definition point offset relative to the <see cref="Center"/>.
		/// </summary>
		public virtual double Offset
		{
			get { return this.SecondPoint.DistanceFrom(this.DefinitionPoint); }
			set
			{
				XYZ dir = this.SecondPoint - this.FirstPoint;
				XYZ v = XYZ.Cross(this.Normal, dir).Normalize(); //Perpendicular to SecondPoint

				this.DefinitionPoint = this.SecondPoint + v * value;
			}
		}

		/// <summary>
		/// Definition point for linear and angular dimensions (in WCS).
		/// </summary>
		[DxfCodeValue(14, 24, 34)]
		public XYZ SecondPoint { get; set; }

		/// <inheritdoc/>
		public override string SubclassMarker => DxfSubclassMarker.Angular2LineDimension;

		/// <summary>
		/// Default constructor.
		/// </summary>
		public DimensionAngular2Line() : base(DimensionType.Angular)
		{
		}

		/// <inheritdoc/>
		public override void ApplyTransform(Transform transform)
		{
			base.ApplyTransform(transform);

			this.FirstPoint = transform.ApplyTransform(this.FirstPoint);
			this.SecondPoint = transform.ApplyTransform(this.SecondPoint);
			this.AngleVertex = transform.ApplyTransform(this.AngleVertex);
			this.DimensionArc = transform.ApplyTransform(this.DimensionArc);
		}

		/// <inheritdoc/>
		public override BoundingBox GetBoundingBox()
		{
			return new BoundingBox(this.FirstPoint, this.SecondPoint);
		}

		/// <inheritdoc/>
		/// <remarks>
		/// For <see cref="DimensionAngular2Line"/> the generation of the block is not yet implemented.
		/// </remarks>
		public override void UpdateBlock()
		{
			// F2D: 두 선의 교점에서 호 점이 있는 쪽 부채꼴을 잰다 (전에는 블록을 만들지 않아 각도 치수가 보이지 않았다)
			base.UpdateBlock();

			XYZ c = this.Center;
			if (c.IsNaN())
			{
				return;
			}
			if (this.Sector() is not { } sec)
			{
				return;
			}
			XYZ best1 = sec.D1, best2 = sec.D2;
			double Proj(XYZ p, XYZ d) => (p - c).Dot(d);
			double t1a = Proj(this.FirstPoint, best1), t1b = Proj(this.SecondPoint, best1);
			double t2a = Proj(this.AngleVertex, best2), t2b = Proj(this.DefinitionPoint, best2);
			this.buildAngularBlock(c, best1, Math.Max(Math.Min(t1a, t1b), 0), Math.Max(Math.Max(t1a, t1b), 0),
				best2, Math.Max(Math.Min(t2a, t2b), 0), Math.Max(Math.Max(t2a, t2b), 0), this.DimensionArc);
		}
	}
}