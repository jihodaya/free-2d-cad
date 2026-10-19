using ACadSharp.Attributes;
using CSMath;
using CSMath.Extensions;
using System;

namespace ACadSharp.Entities
{
	/// <summary>
	/// Represents a <see cref="DimensionAligned"/> entity.
	/// </summary>
	/// <remarks>
	/// Object name <see cref="DxfFileToken.EntityDimension"/> <br/>
	/// Dxf class name <see cref="DxfSubclassMarker.AlignedDimension"/>
	/// </remarks>
	[DxfName(DxfFileToken.EntityDimension)]
	[DxfSubClass(DxfSubclassMarker.AlignedDimension)]
	public class DimensionAligned : Dimension
	{
		/// <summary>
		/// Linear dimension types with an oblique angle have an optional group code 52.
		/// When added to the rotation angle of the linear dimension(group code 50),
		/// it gives the angle of the extension lines
		/// </summary>
		[DxfCodeValue(DxfReferenceType.Optional, 52)]
		public double ExtLineRotation { get; set; }

		/// <summary>
		/// Insertion point for clones of a dimension—Baseline and Continue (in OCS).
		/// </summary>
		[DxfCodeValue(13, 23, 33)]
		public XYZ FirstPoint { get; set; }

		/// <inheritdoc/>
		public override double Measurement
		{
			get
			{
				return this.FirstPoint.DistanceFrom(this.SecondPoint);
			}
		}

		/// <inheritdoc/>
		public override string ObjectName => DxfFileToken.EntityDimension;

		/// <inheritdoc/>
		public override ObjectType ObjectType => ObjectType.DIMENSION_ALIGNED;

		/// <summary>
		/// Definition point offset relative to the <see cref="SecondPoint"/>.
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
		/// Definition point for linear and angular dimensions(in WCS).
		/// </summary>
		[DxfCodeValue(14, 24, 34)]
		public XYZ SecondPoint { get; set; }

		/// <inheritdoc/>
		public override string SubclassMarker => DxfSubclassMarker.AlignedDimension;

		/// <summary>
		/// Default constructor.
		/// </summary>
		public DimensionAligned() : base(DimensionType.Aligned) { }

		/// <summary>
		/// Constructor with the first and second point.
		/// </summary>
		/// <param name="firstPoint"></param>
		/// <param name="secondPoint"></param>
		public DimensionAligned(XYZ firstPoint, XYZ secondPoint) : this()
		{
			this.FirstPoint = firstPoint;
			this.SecondPoint = secondPoint;
		}

		protected DimensionAligned(DimensionType type) : base(type)
		{
			this.DefinitionPoint = this.SecondPoint;
		}

		/// <inheritdoc/>
		public override void ApplyTransform(Transform transform)
		{
			XYZ newNormal = this.transformNormal(transform, this.Normal);
			this.getWorldMatrix(transform, this.Normal, newNormal, out Matrix3 transOW, out Matrix3 transWO);

			base.ApplyTransform(transform);

			this.FirstPoint = this.applyWorldMatrix(this.FirstPoint, transform, transOW, transWO);
			this.SecondPoint = this.applyWorldMatrix(this.SecondPoint, transform, transOW, transWO);
		}

		/// <inheritdoc/>
		protected override XYZ measurementOrigin() => this.FirstPoint;

		/// <inheritdoc/>
		public override BoundingBox GetBoundingBox()
		{
			return new BoundingBox(this.FirstPoint, this.SecondPoint);
		}

		/// <inheritdoc/>
		public override void UpdateBlock()
		{
			base.UpdateBlock();

			XYZ dir = (this.SecondPoint - this.FirstPoint).Normalize();
			XYZ vec = XYZ.Cross(this.Normal, dir).Normalize();

			// F2D: 부호 있는 간격 (Offset은 거리라 항상 양수 → 반대쪽에 놓으면 첫 보조선이 반대편으로 가서 치수선이 기울었다)
			double offset = (this.DefinitionPoint - this.SecondPoint).Dot(vec);
			XYZ dimRef1 = this.FirstPoint + vec * offset;
			XYZ dimRef2 = this.DefinitionPoint;

			// reference points
			this._block.Entities.Add(createDefinitionPoint(FirstPoint));
			this._block.Entities.Add(createDefinitionPoint(SecondPoint));
			this._block.Entities.Add(createDefinitionPoint(dimRef1));
			this._block.Entities.Add(createDefinitionPoint(dimRef2));


			// extension lines
			double thisexo = Math.Sign(offset) * this.ActiveStyle.ExtensionLineOffset * this.ActiveStyle.ScaleFactor;
			double thisexe = Math.Sign(offset) * this.ActiveStyle.ExtensionLineExtension * this.ActiveStyle.ScaleFactor;
			if (!this.ActiveStyle.SuppressFirstExtensionLine)
			{
				{
					XYZ extStart = this.FirstPoint + thisexo * vec;
					XYZ extEnd = dimRef1 + thisexe * vec;
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
					XYZ extStart = this.SecondPoint + thisexo * vec;
					XYZ extEnd = dimRef2 + thisexe * vec;
					// F2D: 측정점이 치수선 위에 있으면 방향이 0 벡터라 NaN 선이 생겼다 → 건너뜀
					if (isFiniteLine(extStart, extEnd))
					{
						this._block.Entities.Add(extensionLine(extStart, extEnd, this.ActiveStyle, this.ActiveStyle.LineTypeExt2));
					}
				}
			}

			// F2D: 치수선·화살촉·문자 배치 (맞춤·문자 위치 규칙, Dimension.layoutLinearDimension)
			this.layoutLinearDimension(dimRef1, dimRef2, (dimRef2 - dimRef1).Normalize());
		}
	}
}