using ACadSharp.Attributes;
using ACadSharp.Tables;
using CSMath;
using CSMath.Extensions;
using System;

namespace ACadSharp.Entities
{
	/// <summary>
	/// Represents a <see cref="DimensionDiameter"/> entity.
	/// </summary>
	/// <remarks>
	/// Object name <see cref="DxfFileToken.EntityDimension"/> <br/>
	/// Dxf class name <see cref="DxfSubclassMarker.DiametricDimension"/>
	/// </remarks>
	[DxfName(DxfFileToken.EntityDimension)]
	[DxfSubClass(DxfSubclassMarker.DiametricDimension)]
	public class DimensionDiameter : Dimension
	{
		/// <summary>
		/// Definition point for diameter, radius, and angular dimensions(in WCS).
		/// </summary>
		[DxfCodeValue(15, 25, 35)]
		public XYZ AngleVertex { get; set; }

		/// <summary>
		/// Gets the center point of the measured arc.
		/// </summary>
		public XYZ Center { get { return this.AngleVertex.Mid(this.DefinitionPoint); } }

		/// <summary>
		/// Leader length for radius and diameter dimensions.
		/// </summary>
		[DxfCodeValue(40)]
		public double LeaderLength { get; set; }

		/// <inheritdoc/>
		public override double Measurement
		{
			get
			{
				return this.DefinitionPoint.DistanceFrom(this.AngleVertex);
			}
		}

		/// <inheritdoc/>
		public override string ObjectName => DxfFileToken.EntityDimension;

		/// <inheritdoc/>
		public override ObjectType ObjectType => ObjectType.DIMENSION_DIAMETER;

		/// <inheritdoc/>
		public override string SubclassMarker => DxfSubclassMarker.DiametricDimension;

		/// <summary>
		/// Default constructor.
		/// </summary>
		public DimensionDiameter() : base(DimensionType.Diameter) { }

		/// <inheritdoc/>
		public override void ApplyTransform(Transform transform)
		{
			base.ApplyTransform(transform);
			this.AngleVertex = transform.ApplyTransform(this.AngleVertex);
			//LeaderLength should be scaled based on axis??
		}

		/// <inheritdoc/>
		public override BoundingBox GetBoundingBox()
		{
			return new BoundingBox(this.InsertionPoint - this.AngleVertex, this.InsertionPoint + this.AngleVertex);
		}

		/// <inheritdoc/>
		public override void UpdateBlock()
		{
			base.UpdateBlock();

			double offset = this.DefinitionPoint.DistanceFrom(this.TextMiddlePoint);
			double radius = (double)this.Measurement * 0.5;

			XY centerRef = this.Center.Convert<XY>();
			XY ref1 = this.AngleVertex.Convert<XY>();
			XY defPoint = this.DefinitionPoint.Convert<XY>();

			double minOffset = (2 * this.ActiveStyle.ArrowSize + this.ActiveStyle.DimensionLineGap) * this.ActiveStyle.ScaleFactor;
			//this.angularBlock(radius, centerRef, ref1, minOffset, true);

			//return;

			double angleRef = defPoint.GetAngle(ref1);
			short inside; // 1 if the dimension line is inside the circumference, -1 otherwise
			if (offset >= radius && offset <= radius + minOffset)
			{
				offset = radius + minOffset;
				inside = -1;
			}
			else if (offset >= radius - minOffset && offset <= radius)
			{
				offset = radius - minOffset;
				inside = 1;
			}
			else if (offset > radius)
			{
				inside = -1;
			}
			else
			{
				inside = 1;
			}

			XY dimRef = XY.Polar(defPoint, offset - this.ActiveStyle.DimensionLineGap * this.ActiveStyle.ScaleFactor, angleRef);

			// reference points
			Layer defPoints = Layer.Defpoints;
			this._block.Entities.Add(new Point(ref1.Convert<XYZ>()) { Layer = defPoints });

			// F2D: 바깥 수평 문자는 기존 CAD처럼 지시선 + 받침선 (원 위 화살촉 하나)
			if (this.radialLanding(centerRef, ref1, angleRef, inside, this.radialText()))
			{
				if (!this.ActiveStyle.CenterMarkSize.IsZero())
				{
					this._block.Entities.AddRange(centerCross(this.Center, radius, this.ActiveStyle));
				}
				return;
			}

			// dimension lines
			if (!this.ActiveStyle.SuppressFirstDimensionLine && !this.ActiveStyle.SuppressSecondDimensionLine)
			{
				if (inside > 0)
				{
					this._block.Entities.Add(dimensionRadialLine(dimRef, ref1, angleRef, (short)inside));
					// F2D: 화살촉 (지름: 원 위 점에서 바깥쪽)
					this._block.Entities.Add(this.radialArrow(ref1, angleRef, inside));
				}
				else
				{
					this._block.Entities.Add(dimensionRadialLine(dimRef, ref1, angleRef, inside));
					this._block.Entities.Add(this.radialArrow(ref1, angleRef, inside));
				}
			}

			// center cross
			if (!this.ActiveStyle.CenterMarkSize.IsZero())
			{
				this._block.Entities.AddRange(centerCross(this.Center, radius, this.ActiveStyle));
			}

			// dimension text
			var text = this.GetMeasurementText(); // F2D: 치수 스타일 형식과 Ø 접두 (예전: 임시 #.##)

			double textRot = angleRef;
			short reverse = 1;
			if (textRot > MathHelper.HalfPI && textRot <= MathHelper.ThreeHalfPI)
			{
				textRot += Math.PI;
				reverse = -1;
			}

			if (!this.IsTextUserDefinedLocation)
			{
				XY textPos = XY.Polar(ref1, -reverse * inside * this.ActiveStyle.DimensionLineGap * this.ActiveStyle.ScaleFactor, textRot);
				this.TextMiddlePoint = textPos.Convert<XYZ>();
			}

			AttachmentPointType attachmentPoint = reverse * inside < 0 ? AttachmentPointType.MiddleLeft : AttachmentPointType.MiddleRight;
			var mText = createTextEntity(this.TextMiddlePoint, text);
			mText.AttachmentPoint = attachmentPoint;

			this._block.Entities.Add(mText);
		}
	}
}