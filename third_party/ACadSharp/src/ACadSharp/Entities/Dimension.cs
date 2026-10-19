using ACadSharp.Attributes;
using ACadSharp.Extensions;
using ACadSharp.Tables;
using ACadSharp.Types.Units;
using ACadSharp.XData;
using CSMath;
using CSMath.Extensions;
using CSUtilities.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACadSharp.Entities;

/// <summary>
/// Represents a <see cref="Dimension"/> entity.
/// </summary>
/// <remarks>
/// Object name <see cref="DxfFileToken.EntityDimension"/> <br/>
/// Dxf class name <see cref="DxfSubclassMarker.Dimension"/>
/// </remarks>
[DxfName(DxfFileToken.EntityDimension)]
[DxfSubClass(DxfSubclassMarker.Dimension)]
public abstract class Dimension : Entity, IOrientable
{
	/// <summary>
	/// Attachment point.
	/// </summary>
	[DxfCodeValue(71)]
	public AttachmentPointType AttachmentPoint { get; set; }

	/// <summary>
	/// Block that contains the entities that make up the dimension picture.
	/// </summary>
	[DxfCodeValue(DxfReferenceType.Name, 2)]
	public BlockRecord Block
	{
		get { return this._block; }
		set
		{
			this._block = updateTableEntry(value, v => this._block = v, this.Document?.BlockRecords);
		}
	}

	/// <summary>
	/// Definition point for the dimension line (in WCS).
	/// </summary>
	[DxfCodeValue(10, 20, 30)]
	public XYZ DefinitionPoint { get; set; }

	/// <summary>
	/// Dimension type.
	/// </summary>
	[DxfCodeValue(70)]
	public DimensionType Flags
	{
		get
		{
			return this._flags;
		}
		internal set
		{
			this._flags = value;
		}
	}

	/// <summary>
	/// Gets or sets a value indicating whether the first arrow.
	/// is to be flipped.
	/// </summary>
	/// <value>
	/// <b>true</b> if the arrow is to be flipped; otherwise, <b>false</b>.
	/// </value>
	/// <remarks>
	/// Arrows are by default drawn inside the extension lines if there is enough
	/// space; otherwise, outside. This flag overrules the standard behavior.
	/// </remarks>
	[DxfCodeValue(74)]
	public bool FlipArrow1 { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the second arrow
	/// to be flipped.
	/// </summary>
	/// <value>
	/// <b>true</b> if the arrow is to be flipped; otherwise, <b>false</b>.
	/// </value>
	/// <remarks>
	/// Arrows are by default drawn inside the extension lines if there is enough
	/// space; otherwise, outside. This flag overrules the standard behavior.
	/// </remarks>
	[DxfCodeValue(75)]
	public bool FlipArrow2 { get; set; }

	/// <summary>
	/// Gets a value indicating whether a style override is present in the extended data.
	/// </summary>
	/// <remarks>Use this property to determine if the object contains custom style information that overrides
	/// default styling. This can be useful when rendering or processing objects that may have user-defined appearance
	/// settings.</remarks>
	public bool HasStyleOverride
	{
		get
		{
			if (this.ExtendedData.TryGet(AppId.DefaultName, out XData.ExtendedData edata))
			{
				var header = edata.Records.FirstOrDefault() as XData.ExtendedDataString;
				if (header == null || header.Value != DimensionStyle.StyleOverrideEntryName)
				{
					return false;
				}
				else
				{
					return true;
				}
			}
			else
			{
				return false;
			}
		}
	}

	/// <summary>
	/// All dimension types have an optional 51 group code, which indicates the horizontal direction for the dimension entity.The dimension entity determines the orientation of dimension text and lines for horizontal, vertical, and rotated linear dimensions
	/// This group value is the negative of the angle between the OCS X axis and the UCS X axis. It is always in the XY plane of the OCS
	/// </summary>
	[DxfCodeValue(DxfReferenceType.Optional | DxfReferenceType.IsAngle, 51)]
	public double HorizontalDirection { get; set; }

	/// <summary>
	/// Insertion point for clones of a dimension-Baseline and Continue(in OCS).
	/// </summary>
	[DxfCodeValue(12, 22, 32)]
	public XYZ InsertionPoint { get; set; }

	/// <summary>
	/// Indicates if the dimension is angular or linear.
	/// </summary>
	// F2D: 종류는 아래 4비트 값 (Diameter=3도 Angular=2 비트를 가져 HasFlag로는 각도로 오인됐다)
	public bool IsAngular { get { var kind = (DimensionType)((int)this.Flags & 0x0F); return kind == DimensionType.Angular3Point || kind == DimensionType.Angular; } }

	/// <summary>
	/// Indicates if the dimension text has been positioned at a user-defined location rather than at the default location.
	/// </summary>
	public bool IsTextUserDefinedLocation
	{
		get
		{
			return this._flags.HasFlag(DimensionType.TextUserDefinedLocation);
		}
		set
		{
			if (value)
			{
				this._flags.AddFlag(DimensionType.TextUserDefinedLocation);
			}
			else
			{
				this._flags.RemoveFlag(DimensionType.TextUserDefinedLocation);
			}
		}
	}

	/// <summary>
	/// Dimension text-line spacing factor.
	/// </summary>
	/// <remarks>
	/// Percentage of default (3-on-5) line spacing to be applied.
	/// </remarks>
	/// <value>
	/// Valid values range from 0.25 to 4.00
	/// </value>
	[DxfCodeValue(DxfReferenceType.Optional, 41)]
	public double LineSpacingFactor { get; set; }

	/// <summary>
	/// Dimension text line-spacing style.
	/// </summary>
	[DxfCodeValue(DxfReferenceType.Optional, 72)]
	public LineSpacingStyleType LineSpacingStyle { get; set; } = LineSpacingStyleType.AtLeast; //DWG 규격상 1 또는 2만 유효

	/// <summary>
	/// Actual measurement.
	/// </summary>
	[DxfCodeValue(DxfReferenceType.Optional, 42)]
	public abstract double Measurement { get; }

	/// <inheritdoc/>
	[DxfCodeValue(210, 220, 230)]
	public XYZ Normal { get; set; } = XYZ.AxisZ;

	/// <summary>
	/// Dimension style.
	/// </summary>
	[DxfCodeValue(DxfReferenceType.Name, 3)]
	public DimensionStyle Style
	{
		get { return this._style; }
		set
		{
			if (value == null)
			{
				throw new ArgumentNullException(nameof(value));
			}

			this._style = this.updateTableEntry(value, v => this._style = v, this.Document?.DimensionStyles);
		}
	}

	/// <inheritdoc/>
	public override string SubclassMarker => DxfSubclassMarker.Dimension;

	/// <summary>
	/// Gets or sets an explicit dimension text to be displayed instead of the standard
	/// dimension text created from the measurement in the format specified by the
	/// dimension-style properties.
	/// </summary>
	/// <remarks>
	/// If null or empty, the dimension created from the measurement is to be displayed.
	/// If " " (one blank space), the text is to be suppressed. Anything else is drawn as the text.
	/// </remarks>
	[DxfCodeValue(DxfReferenceType.Optional, 1)]
	public string Text { get; set; }

	/// <summary>
	/// Middle point of dimension text(in OCS).
	/// </summary>
	[DxfCodeValue(11, 21, 31)]
	public XYZ TextMiddlePoint { get; set; }

	/// <summary>
	/// Rotation angle of the dimension text away from its default orientation (the direction of the dimension line).
	/// </summary>
	/// <remarks>
	/// Optional
	/// </remarks>
	[DxfCodeValue(DxfReferenceType.Optional | DxfReferenceType.IsAngle, 53)]
	public double TextRotation { get; set; }

	/// <summary>
	/// Version number.
	/// </summary>
	[DxfCodeValue(280)]
	public byte Version { get; set; }

	/// <summary>F2D: 선의 두 끝점이 모두 유한한 값이고 길이가 0이 아닌지.</summary>
	protected static bool isFiniteLine(XYZ a, XYZ b)
	{
		bool finite = double.IsFinite(a.X) && double.IsFinite(a.Y) && double.IsFinite(a.Z)
			&& double.IsFinite(b.X) && double.IsFinite(b.Y) && double.IsFinite(b.Z);
		return finite && !a.Equals(b);
	}

	protected BlockRecord _block;

	//This group value is the negative of the angle between the OCS X axis and the UCS X axis.It is always in the XY plane of the OCS
	protected DimensionType _flags;

	private DimensionStyle _style = DimensionStyle.Default;

	/// <summary>
	/// F2D: 블록을 만드는 동안 쓰는 스타일 (DSTYLE 재정의를 적용한 스타일). <see cref="createBlock"/>에서 정한다.
	/// </summary>
	private DimensionStyle _activeStyle;

	/// <summary>
	/// F2D: 재정의를 적용한 현재 스타일 (블록 생성용).
	/// </summary>
	protected DimensionStyle ActiveStyle => this._activeStyle ?? this.Style;

	protected Dimension(DimensionType type)
	{
		this._flags = type;
		this._flags |= DimensionType.BlockReference;
	}

	/// <inheritdoc/>
	public override void ApplyTransform(Transform transform)
	{
		XYZ newNormal = this.transformNormal(transform, this.Normal);
		this.getWorldMatrix(transform, this.Normal, newNormal, out Matrix3 transOW, out Matrix3 transWO);

		this.DefinitionPoint = this.applyWorldMatrix(this.DefinitionPoint, transform, transOW, transWO);

		if (this.IsTextUserDefinedLocation)
		{
			this.TextMiddlePoint = this.applyWorldMatrix(this.TextMiddlePoint, transform, transOW, transWO);
		}

		this.Normal = newNormal;
	}

	/// <inheritdoc/>
	public override CadObject Clone()
	{
		Dimension clone = (Dimension)base.Clone();

		clone._style = this.Style.CloneTyped();
		clone._block = this.Block?.CloneTyped();

		return clone;
	}

	/// <summary>
	/// Retrieves the currently active dimension style, including any style overrides that are applied.
	/// </summary>
	/// <remarks>Use this method to obtain the effective dimension style for rendering or measurement purposes.
	/// The returned style reflects all current overrides and can differ from the base style if overrides are
	/// set.</remarks>
	/// <returns>A <see cref="DimensionStyle"/> instance representing the active dimension style. If no overrides are present,
	/// returns the base style; otherwise, returns a copy of the style with overrides applied.</returns>
	public DimensionStyle GetActiveDimensionStyle()
	{
		return ActiveStyleOf(this, this.Style);
	}

	/// <summary>
	/// F2D: 객체(치수·지시선)의 DSTYLE 재정의(XData "ACAD")를 스타일에 적용한 복사본. 재정의가 없으면 스타일 그대로.
	/// </summary>
	public static DimensionStyle ActiveStyleOf(CadObject owner, DimensionStyle baseStyle)
	{
		if (baseStyle == null)
		{
			return null;
		}
		DxfClassMap overrideMap = StyleOverrideMapOf(owner);
		if (overrideMap == null || overrideMap.DxfProperties.Count == 0)
		{
			return baseStyle;
		}

		// F2D: 얕은 복사 (참조 객체는 원본 그대로: 문자 스타일·화살촉 블록·선종류를 복제하지 않는다)
		DimensionStyle style = (DimensionStyle)baseStyle.CloneTyped();
		style.Name = "override";
		if (baseStyle.Style != null) style.Style = baseStyle.Style;
		style.LeaderArrow = baseStyle.LeaderArrow;
		style.ArrowBlock = baseStyle.ArrowBlock;
		style.DimArrow1 = baseStyle.DimArrow1;
		style.DimArrow2 = baseStyle.DimArrow2;
		style.LineType = baseStyle.LineType;
		style.LineTypeExt1 = baseStyle.LineTypeExt1;
		style.LineTypeExt2 = baseStyle.LineTypeExt2;

		foreach (KeyValuePair<int, DxfProperty> item in overrideMap.DxfProperties)
		{
			try
			{
				ApplyStyleOverrideValue(style, item.Key, item.Value.StoredValue, owner.Document);
			}
			catch (Exception)
			{
				// 알 수 없는 값은 무시 (원래 스타일 값 유지)
			}
		}

		return style;
	}

	/// <summary>
	/// F2D: DSTYLE 재정의 값 하나를 치수 스타일에 적용한다. 색상(176/177/178/69)은 색상 번호, 핸들(340~347)은 문서에서 찾은 객체.
	/// </summary>
	public static void ApplyStyleOverrideValue(DimensionStyle style, int code, object value, CadDocument doc)
	{
		if (value == null)
		{
			return;
		}

		switch (code)
		{
			case 176: style.DimensionLineColor = new Color(System.Convert.ToInt16(value)); return;
			case 177: style.ExtensionLineColor = new Color(System.Convert.ToInt16(value)); return;
			case 178: style.TextColor = new Color(System.Convert.ToInt16(value)); return;
			case 70: style.TextBackgroundColor = new Color(System.Convert.ToInt16(value)); return;
			case 340:
			case 341:
			case 342:
			case 343:
			case 344:
			case 345:
			case 346:
			case 347:
				{
					ulong handle = System.Convert.ToUInt64(value);
					CadObject obj = handle == 0 ? null : doc?.GetCadObject(handle);
					switch (code)
					{
						case 340: if (obj is TextStyle ts) style.Style = ts; break;
						case 341: style.LeaderArrow = obj as BlockRecord; break;
						case 342: style.ArrowBlock = obj as BlockRecord; break;
						case 343: style.DimArrow1 = obj as BlockRecord; break;
						case 344: style.DimArrow2 = obj as BlockRecord; break;
						case 345: style.LineType = obj as LineType; break;
						case 346: style.LineTypeExt1 = obj as LineType; break;
						case 347: style.LineTypeExt2 = obj as LineType; break;
					}
					return;
				}
		}

		DxfClassMap styleMap = DxfClassMap.Create<DimensionStyle>();
		if (!styleMap.DxfProperties.TryGetValue(code, out DxfProperty p))
		{
			return;
		}
		var type = p.GetValue(style)?.GetType();
		if (type == typeof(double)) value = System.Convert.ToDouble(value);
		else if (type == typeof(short)) value = System.Convert.ToInt16(value);
		else if (type == typeof(int)) value = System.Convert.ToInt32(value);
		else if (type == typeof(string)) value = System.Convert.ToString(value);
		p.SetValue(code, style, value);
	}

	/// <summary>
	/// Get the measurement text from the actual <see cref="Dimension.Measurement"/> value.
	/// </summary>
	/// <returns></returns>
	public string GetMeasurementText()
	{
		return this.GetMeasurementText(this.GetActiveDimensionStyle());
	}

	/// <summary>
	/// Get the measurement text from the actual <see cref="Dimension.Measurement"/> value.
	/// </summary>
	/// <param name="style">style to apply to the text.</param>
	/// <returns></returns>
	public string GetMeasurementText(DimensionStyle style)
	{
		string text = string.Empty;
		double value = style.ApplyRounding(this.Measurement);

		UnitStyleFormat unitFormat = style.GetUnitStyleFormat();

		if (this.IsAngular && this is not DimensionArc) // F2D: 호 길이 치수는 길이로
		{
			switch (style.AngularUnit)
			{
				case AngularUnitFormat.DecimalDegrees:
					text = unitFormat.ToDegrees(value);
					break;
				case AngularUnitFormat.DegreesMinutesSeconds:
					text = unitFormat.ToDegreesMinutesSeconds(value);
					break;
				case AngularUnitFormat.Gradians:
					text = unitFormat.ToGradians(value);
					break;
				case AngularUnitFormat.Radians:
					text = unitFormat.ToRadians(value);
					break;
				case AngularUnitFormat.SurveyorsUnits:
				default:
					text = unitFormat.ToDecimal(value, true);
					break;
			}
		}
		else
		{
			var scaledValue = value * style.LinearScaleFactor;
			switch (style.LinearUnitFormat)
			{
				case LinearUnitFormat.Scientific:
					text = unitFormat.ToScientific(scaledValue);
					break;
				case LinearUnitFormat.Engineering:
					text = unitFormat.ToEngineering(scaledValue);
					break;
				case LinearUnitFormat.Architectural:
					text = unitFormat.ToArchitectural(scaledValue);
					break;
				case LinearUnitFormat.Fractional:
					text = unitFormat.ToFractional(scaledValue);
					break;
				case LinearUnitFormat.None:
				case LinearUnitFormat.Decimal:
				case LinearUnitFormat.WindowsDesktop:
				default:
					text = unitFormat.ToDecimal(scaledValue);
					break;
			}
		}

		string prefix = string.Empty;
		switch (this.Flags & (DimensionType)0x0F) // F2D: 블록 참조(32)·사용자 문자 위치(128) 비트가 있어도 반지름·지름 접두어
		{
			case DimensionType.Diameter:
				prefix = string.IsNullOrEmpty(style.Prefix) ? "∅" : style.Prefix; // F2D: 기존 CAD와 같은 ∅ (U+2205)
				break;
			case DimensionType.Radius:
			case (DimensionType)9: // F2D: 꺾기 반지름 치수 (LARGE_RADIAL_DIMENSION)
				prefix = string.IsNullOrEmpty(style.Prefix) ? "R" : style.Prefix;
				break;
			default:
				prefix = string.IsNullOrEmpty(style.Prefix) ? string.Empty : style.Prefix;
				break;
		}
		if (this is DimensionArc)
		{
			prefix = "⌒" + prefix; // F2D: 호 길이 기호 (DIMARCSYM 0 = 문자 앞)
		}

		var valueText = $"{prefix}{text}{style.Suffix}";

		// F2D: 한계·공차·대체 단위 (기존 CAD가 다시 그린 치수 블록과 같은 MText 형식)
		if (!this.IsAngular || this is DimensionArc)
		{
			double lfac = style.LinearScaleFactor;
			if (style.LimitsGeneration)
			{
				// 한계: 위 = 값 + 상한, 아래 = 값 - 하한 (기본 정밀도) → \S위^아래;
				string hi = formatLinear(style, unitFormat, (value + style.PlusTolerance) * lfac);
				string lo = formatLinear(style, unitFormat, (value - style.MinusTolerance) * lfac);
				valueText = $"{prefix}{toleranceHeight(style)}\\S{hi}^{lo};{closeHeight(style)}{style.Suffix}";
			}
			else if (style.GenerateTolerances)
			{
				var tolFormat = style.GetUnitStyleFormat();
				tolFormat.LinearDecimalPlaces = style.ToleranceDecimalPlaces;
				tolFormat.LinearZeroHandling = style.ToleranceZeroHandling;
				double tp = style.PlusTolerance * lfac, tm = style.MinusTolerance * lfac;
				if (Math.Abs(tp - tm) < 1e-12)
				{
					// 대칭: 값±공차
					valueText = $"{prefix}{text}%%p{formatLinear(style, tolFormat, Math.Abs(tp))}{style.Suffix}";
				}
				else
				{
					// 편차: 값\S+상한^-하한; (0은 부호 없이)
					string Signed(double v) => Math.Abs(v) < 1e-12 ? formatLinear(style, tolFormat, 0) : (v > 0 ? "+" : "-") + formatLinear(style, tolFormat, Math.Abs(v));
					valueText = $"{prefix}{text}{toleranceHeight(style)}\\S{Signed(tp)}^{Signed(-tm)};{closeHeight(style)}{style.Suffix}";
				}
			}

			if (style.AlternateUnitDimensioning)
			{
				// 대체 단위: " [대체 값]" — DIMAPOST의 []가 값 자리 (없으면 뒤에 붙임)
				var altFormat = style.GetUnitStyleFormat();
				altFormat.LinearDecimalPlaces = style.AlternateUnitDecimalPlaces;
				altFormat.LinearZeroHandling = style.AlternateUnitZeroHandling;
				double alt = style.ApplyRounding(this.Measurement * lfac * style.AlternateUnitScaleFactor, true);
				string altText = formatLinear(style, altFormat, alt, style.AlternateUnitFormat);
				string post = style.AlternateDimensioningSuffix ?? string.Empty;
				int at = post.IndexOf("[]", StringComparison.Ordinal);
				altText = at >= 0 ? post.Substring(0, at) + altText + post.Substring(at + 2) : altText + post;
				valueText += $" [{altText}]";
			}
		}

		if (!string.IsNullOrEmpty(this.Text))
		{
			var styledText = this.Text;
			styledText = styledText.Replace("<>", valueText);
			return styledText;
		}

		return valueText;
	}

	/// <summary>F2D: 길이 값을 단위 형식에 맞춰 문자로.</summary>
	private static string formatLinear(DimensionStyle style, UnitStyleFormat format, double v, LinearUnitFormat? unit = null)
	{
		switch (unit ?? style.LinearUnitFormat)
		{
			case LinearUnitFormat.Scientific:
				return format.ToScientific(v);
			case LinearUnitFormat.Engineering:
				return format.ToEngineering(v);
			case LinearUnitFormat.Architectural:
				return format.ToArchitectural(v);
			case LinearUnitFormat.Fractional:
				return format.ToFractional(v);
			default:
				return format.ToDecimal(v);
		}
	}

	/// <summary>F2D: 공차 문자 높이 배율 (DIMTFAC가 1이 아니면 {\H배율x; ... }).</summary>
	private static string toleranceHeight(DimensionStyle style)
	{
		double f = style.ToleranceScaleFactor;
		return f > 0 && Math.Abs(f - 1) > 1e-9 ? "{\\H" + f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "x;" : string.Empty;
	}

	private static string closeHeight(DimensionStyle style)
	{
		double f = style.ToleranceScaleFactor;
		return f > 0 && Math.Abs(f - 1) > 1e-9 ? "}" : string.Empty;
	}

	/// <summary>
	/// Retrieves a map of style override properties from the extended data, if present and valid.
	/// </summary>
	/// <remarks>This method inspects the extended data for a section identified as a style override ("DSTYLE").
	/// If the section is not present or is malformed, the method returns <see langword="null"/>. The returned map can be
	/// used to access or apply dimension style overrides defined in the extended data.</remarks>
	/// <returns>A <see cref="DxfClassMap"/> containing the style override properties if the extended data includes a valid style
	/// override section; otherwise, <see langword="null"/>.</returns>
	public DxfClassMap GetStyleOverrideMap()
	{
		return StyleOverrideMapOf(this);
	}

	/// <summary>F2D: 객체(치수·지시선)의 DSTYLE 재정의 맵 (없으면 null).</summary>
	public static DxfClassMap StyleOverrideMapOf(CadObject owner)
	{
		if (owner == null || !owner.ExtendedData.TryGet(AppId.DefaultName, out XData.ExtendedData edata))
		{
			return null;
		}

		var header = edata.Records.FirstOrDefault() as XData.ExtendedDataString;
		if (header == null || header.Value != DimensionStyle.StyleOverrideEntryName)
		{
			return null;
		}

		DxfClassMap styleMap = DxfClassMap.Create<DimensionStyle>();

		DxfClassMap map = new DxfClassMap();
		map.Name = header.Value;

		var values = edata.Records
			.SkipWhile(c => c is not XData.ExtendedDataControlString)
			.Skip(1)
			.TakeWhile(c => c is not XData.ExtendedDataControlString)
			.ToArray();

		if (values.Length % 2 != 0)
		{
			//Check dxf code | value pairs
			return null;
		}

		for (int i = 0; i < values.Length; i++)
		{
			XData.ExtendedDataInteger16 code = values[i] as XData.ExtendedDataInteger16;
			i++;
			XData.ExtendedDataRecord value = values[i];
			DxfProperty prop = styleMap.DxfProperties[code.Value];
			prop.StoredValue = value.RawValue;

			map.DxfProperties.Add(code.Value, prop);
		}

		return map;
	}

	/// <summary>
	/// Applies the specified dimension style override to the current object, updating its extended data to reflect the
	/// overridden properties.
	/// </summary>
	/// <remarks>This method updates the object's extended data to record the overridden dimension style
	/// properties. Properties that are identical between the current style and the override are not affected. Use this
	/// method to persist style overrides for dimension objects in environments that support extended data.</remarks>
	/// <param name="styleOverride">The dimension style containing property values to override the current object's style. Only properties with values
	/// different from the current style are applied.</param>
	public void SetDimensionOverride(DimensionStyle styleOverride)
	{
		DxfClassMap styleMap = DxfClassMap.Create<DimensionStyle>();
		styleMap.DxfProperties.Remove(2);
		styleMap.DxfProperties.Remove(70);

		DxfClassMap overrideMap = new DxfClassMap();
		foreach (KeyValuePair<int, DxfProperty> item in styleMap.DxfProperties)
		{
			var curr = item.Value.GetRawValue(this.Style);
			var over = item.Value.GetRawValue(styleOverride);

			// over=null means "reset to default": ToXDataRecords emits nothing for a null
			// StoredValue, so skip to avoid adding a useless entry to the override map.
			if (over == null)
				continue;

			// object.Equals handles curr=null safely:
			//   (null, null)   → true  → already skipped above (over would be null)
			//   (null, value)  → false → include (null→value: new assignment)
			//   (value, value) → delegates to curr.Equals(over)
			if (!object.Equals(curr, over))
			{
				item.Value.StoredValue = over;
				overrideMap.DxfProperties.Add(item.Key, item.Value);
			}
		}

		this.SetStyleOverrideMap(overrideMap);
	}

	/// <summary>
	/// Sets the style override mapping for the current object using the specified DXF class map.
	/// </summary>
	/// <remarks>This method updates the object's extended data to reflect the provided style overrides. Existing
	/// style override entries are cleared before applying the new map. Use this method to programmatically control
	/// dimension style overrides in the object's extended data.</remarks>
	/// <param name="map">A DXF class map containing the property overrides to apply. Each property in the map defines a style override
	/// entry.</param>
	public void SetStyleOverrideMap(DxfClassMap map)
	{
		XData.ExtendedData edata = this.ExtendedData.TryAdd(AppId.DefaultName, new ExtendedData());
		edata.Records.Clear();

		edata.Records.Add(new ExtendedDataString(DimensionStyle.StyleOverrideEntryName));
		edata.Records.Add(new ExtendedDataControlString(false));
		foreach (DxfProperty p in map.DxfProperties.Values)
		{
			edata.Records.AddRange(p.ToXDataRecords());
		}
		edata.Records.Add(new ExtendedDataControlString(true));
	}

	/// <summary>
	/// Updates the block that represents this dimension.
	/// </summary>
	public virtual void UpdateBlock()
	{
		this.createBlock();
	}

	internal override void AssignDocument(CadDocument doc)
	{
		base.AssignDocument(doc);

		this._style = this.updateTableEntry(this._style, l => this._style = l, doc.DimensionStyles);
		this._block = this.updateTableEntry(this._block, b => this._block = b, doc.BlockRecords);

		if (this._block != null)
		{
			this._block.Name = this.generateBlockName();
		}
	}

	internal override void UnassignDocument()
	{
		this.Document.DimensionStyles.RemoveReference(this.Style.Name, this);
		this.Document.BlockRecords.RemoveReference(this.Block?.Name, this);

		base.UnassignDocument();

		this._style = (DimensionStyle)this.Style?.Clone();
		this._block = (BlockRecord)this.Block?.Clone();
	}

	/// <summary>
	/// F2D: 치수 문자 크기 재기 (앱이 실제 글꼴로 재도록 정한다). 없으면 글자 수로 어림.
	/// 돌려주는 값은 회전 전 (폭, 높이).
	/// </summary>
	public static Func<MText, CadDocument, (double Width, double Height)?> TextMeasurer { get; set; }

	/// <summary>F2D: 치수 문자 상자 (폭, 높이) = 그린 글자 모양의 범위 (기존 CAD 블록 값과 같음).</summary>
	protected (double Width, double Height) measureDimText(MText m)
	{
		double h = m.Height;
		(double Width, double Height)? r = null;
		try
		{
			r = TextMeasurer?.Invoke(m, this.Document);
		}
		catch (Exception)
		{
			r = null;
		}
		double w;
		if (r.HasValue && r.Value.Width > 0)
		{
			w = r.Value.Width;
		}
		else
		{
			// 서식 코드(\A1; {\f...;} 등)를 빼고 글자 수로 어림
			string plain = System.Text.RegularExpressions.Regex.Replace(m.Value ?? "", @"\\[A-Za-z][^;\\]*;|[{}]|%%[CcDdPp]", "x");
			w = Math.Max(1, plain.Length) * h * 0.7;
		}
		// 높이: 잰 글자 모양 높이 (기존 CAD도 글자 모양 기준, "1.0" 3.5 → 3.636), 못 재면 문자 높이 × 1.0333
		double bh = r.HasValue && r.Value.Height > 0 ? r.Value.Height : h * 1.0333;
		return (w, bh);
	}

	/// <summary>
	/// F2D: 선형·정렬 치수의 치수선·화살촉·문자 배치 (기존 CAD 규칙).
	/// r1→r2 = 치수선 양 끝(보조선과 만나는 점), dir = r1→r2 단위 벡터.
	/// - 문자 방향: 안쪽은 DIMTIH, 바깥은 DIMTOH가 켜지면 수평, 아니면 치수선 방향(읽기 쉬운 쪽).
	/// - 수평 문자인데 치수선이 수평이 아니거나 DIMTAD 중앙이면 문자를 치수선 위 가운데에 두고 그만큼 치수선을 끊는다.
	///   그 밖은 DIMTAD(위·밖·JIS·아래)에 따라 간격 + 문자 상자 높이/2 만큼 띄운다.
	/// - 맞춤(DIMATFIT·DIMTIX): 문자·화살촉이 보조선 사이에 안 들어가면 화살촉을 밖으로(보조선 쪽을 가리키게) 하고
	///   화살촉 길이만큼 바깥 선을 긋는다. 문자가 밖에 있으면 치수선을 문자 아래까지 늘인다.
	/// - 기본 화살촉(닫고 채움)은 치수선이 화살촉 밑변에서 시작한다 (기존 CAD 블록과 같음).
	/// </summary>
	protected void layoutLinearDimension(XYZ r1, XYZ r2, XYZ dir)
	{
		var st = this.ActiveStyle;
		double scale = st.ScaleFactor > 0 ? st.ScaleFactor : 1.0;
		double asz = st.ArrowSize * scale;
		double gap = Math.Abs(st.DimensionLineGap) * scale;
		double len = (r2 - r1).GetLength();
		if (len < 1e-12 || double.IsNaN(dir.X))
		{
			dir = XYZ.AxisX;
		}
		double lineAng = Math.Atan2(dir.Y, dir.X);
		double readAng = lineAng;
		while (readAng > Math.PI / 2 + 1e-9) readAng -= Math.PI;
		while (readAng <= -Math.PI / 2 + 1e-9) readAng += Math.PI;
		bool lineHorizontal = Math.Abs(Math.Sin(lineAng)) < 1e-6;

		string text = this.GetMeasurementText();
		MText mText = this.createTextEntity(XYZ.Zero, text);
		mText.Height = st.TextHeight * scale;
		var (tw, th) = this.measureDimText(mText);
		bool hasText = text.Trim().Length > 0;
		bool basic = st.DimensionLineGap < 0 && hasText; // 기본 치수: 문자 둘레 상자
		double textOff = basic ? 2 * gap + th / 2 : gap + th / 2;

		double TextRot(bool inside)
		{
			if (Math.Abs(this.TextRotation) > 1e-12) return this.TextRotation;
			bool horiz = inside ? st.TextInsideHorizontal : st.TextOutsideHorizontal;
			return horiz ? 0.0 : readAng;
		}
		// 치수선 방향으로 본 문자 길이
		double Extent(double rot) => Math.Abs(tw * Math.Cos(rot - lineAng)) + Math.Abs(th * Math.Sin(rot - lineAng));

		double needText = hasText ? Extent(TextRot(true)) + 2 * gap : 0;
		double needArrows = 2 * asz;
		bool textInside, arrowsInside;
		if (len >= needText + needArrows)
		{
			textInside = true;
			arrowsInside = true;
		}
		else if (st.TextInsideExtensions)
		{
			textInside = true;
			arrowsInside = false;
		}
		else
		{
			switch ((int)st.DimensionTextArrowFit) // DIMATFIT (289)
			{
				case 1: // 화살촉 먼저
					arrowsInside = len >= needArrows;
					textInside = false;
					break;
				case 2: // 문자 먼저
					textInside = len >= needText;
					arrowsInside = false;
					break;
				case 3: // 최적 맞춤
					textInside = len >= needText;
					arrowsInside = !textInside && len >= needArrows;
					break;
				default: // 문자와 화살촉 모두 밖
					textInside = false;
					arrowsInside = false;
					break;
			}
		}

		var b1 = FirstArrowBlock(st);
		var b2 = SecondArrowBlock(st);
		XYZ? landingStart = null;
		double landingWidth = 0;

		// 문자 위치: 사용자가 놓은 곳이 있으면 그대로, 없으면 가운데(안) 또는 둘째 보조선 바깥(밖)
		XYZ up = XYZ.Cross(this.Normal, dir).Normalize();
		XYZ center;
		double rot;
		bool onLine;
		if (this.IsTextUserDefinedLocation)
		{
			// 기존 CAD: 사용자가 옮긴 문자 위치(TextMiddlePoint)는 치수선 위의 점이고, 그리는 문자는 DIMTAD만큼 띄운다
			XYZ userPt = this.TextMiddlePoint;
			double t = (userPt - r1).Dot(dir);
			bool inside = t >= 0 && t <= len;
			rot = TextRot(inside);
			bool horizontalText = Math.Abs(rot) < 1e-12;
			onLine = st.TextVerticalAlignment == DimensionTextVerticalAlignment.Centered || (horizontalText && !lineHorizontal);
			center = onLine ? userPt : offsetText(userPt, rot, textOff, up);
		}
		else
		{
			rot = TextRot(textInside);
			double ext = Extent(rot);
			bool horizontalText = Math.Abs(rot) < 1e-12;
			onLine = st.TextVerticalAlignment == DimensionTextVerticalAlignment.Centered || (horizontalText && !lineHorizontal);
			// 밖에 둘 때 (띄운 문자): 화살촉 다음, 그 밑으로 치수선이 지나감
			XYZ basePt = textInside
				? r1 + (r2 - r1) * 0.5
				: r2 + dir * (asz + gap + ext / 2);
			if (!textInside && horizontalText && !lineHorizontal)
			{
				// 기존 CAD: 수평 문자를 기울어진(수직) 치수선 바깥에 둘 때는 짧은 선 끝에서 오른쪽으로 받침선을 긋고 그 위에 문자
				landingStart = r2 + dir * (arrowsInside ? asz : (b2 == null ? asz : 0) + asz);
				landingWidth = gap + tw;
				basePt = landingStart.Value + XYZ.AxisX * (gap + tw / 2) + XYZ.AxisY * (gap + th / 2);
				onLine = false;
				rot = 0;
			}
			if (!onLine && !landingStart.HasValue)
			{
				basePt = offsetText(basePt, rot, textOff, up);
			}
			center = basePt;
			this.TextMiddlePoint = center;
		}

		// 문자가 치수선 방향으로 차지하는 구간 [a, b] (r1 기준)
		double tc = (center - r1).Dot(dir);
		double half = hasText ? Extent(rot) / 2 : 0;
		double ta = tc - half, tb = tc + half;

		// 치수선 구간 계산
		bool sd1 = st.SuppressFirstDimensionLine, sd2 = st.SuppressSecondDimensionLine;
		var segs = new List<(double A, double B)>();
		if (arrowsInside)
		{
			double a = b1 == null ? asz : 0;
			double b = len - (b2 == null ? asz : 0);
			if (b > a) segs.Add((a, b));
		}
		else
		{
			// 바깥 화살촉: 밑변에서 화살촉 길이만큼 바깥으로
			double o1 = b1 == null ? -asz : 0;
			double o2 = b2 == null ? len + asz : len;
			segs.Add((o1 - asz, o1));
			segs.Add((o2, o2 + asz));
			if (st.TextOutsideExtensions)
			{
				segs.Add((0, len)); // DIMTOFL: 보조선 사이에도 치수선
			}
		}
		// 문자가 보조선 바깥에 있으면 그쪽 치수선을 문자 끝 + 간격까지 늘린다
		// (화살촉이 밖이면 그 짧은 선을, 안이면 보조선에서부터)
		if (landingStart.HasValue)
		{
			if (arrowsInside) segs.Add((len, len + asz));
		}
		else if (hasText && (tb < 0 || ta > len))
		{
			bool left = tc < 0;
			double reach = onLine
				? (left ? tb + gap : ta - gap)   // 선 위 문자: 문자 앞까지
				: (left ? ta - gap : tb + gap);  // 띄운 문자: 문자 밑을 지나 끝까지
			if (arrowsInside)
			{
				if (left) segs.Add((Math.Min(reach, 0), 0)); else segs.Add((len, Math.Max(reach, len)));
			}
			else
			{
				int i = left ? 0 : 1;
				var (a, b) = segs[i];
				segs[i] = left ? (Math.Min(a, reach), b) : (a, Math.Max(b, reach));
			}
		}
		// 문자를 선 위 가운데에 두면 그 구간을 끊는다
		if (hasText && onLine && !landingStart.HasValue)
		{
			double ca = ta - (basic ? 2 : 1) * gap, cb = tb + (basic ? 2 : 1) * gap;
			var cut = new List<(double A, double B)>();
			foreach (var (a, b) in segs)
			{
				if (b <= ca || a >= cb) { cut.Add((a, b)); continue; }
				if (a < ca) cut.Add((a, ca));
				if (b > cb) cut.Add((cb, b));
			}
			segs = cut;
		}
		// 치수선 1·2 끄기 (가운데 기준 반쪽씩)
		double mid = len / 2;
		foreach (var (a0, b0) in segs)
		{
			double a = a0, b = b0;
			if (b - a < 1e-9) continue;
			if (sd1 && sd2) break;
			if (sd1) a = Math.Max(a, mid);
			if (sd2) b = Math.Min(b, mid);
			if (b - a < 1e-9) continue;
			this._block.Entities.Add(dimensionLine(r1 + dir * a, r1 + dir * b, st));
		}

		if (landingStart.HasValue && !sd2)
		{
			this._block.Entities.Add(dimensionLine(landingStart.Value, landingStart.Value + XYZ.AxisX * landingWidth, st));
		}

		// 화살촉
		if (!sd1) this._block.Entities.Add(this.dimensionArrow(r1, arrowsInside ? -dir : dir, st, b1));
		if (!sd2) this._block.Entities.Add(this.dimensionArrow(r2, arrowsInside ? dir : -dir, st, b2));

		// 문자
		if (hasText)
		{
			mText.InsertPoint = center;
			mText.AlignmentPoint = new XYZ(Math.Cos(rot), Math.Sin(rot), 0);
			this._block.Entities.Add(mText);
			if (basic)
			{
				this.addTextBox(center, rot, tw / 2 + gap, th / 2 + gap, st);
			}
		}
	}

	/// <summary>F2D: 기본 치수 상자 (기존 CAD: 선 네 개, 문자 가운데 ± (폭/2 + 간격, 높이/2 + 간격)).</summary>
	private void addTextBox(XYZ center, double rot, double hw, double hh, DimensionStyle st)
	{
		XYZ ux = new XYZ(Math.Cos(rot), Math.Sin(rot), 0);
		XYZ uy = new XYZ(-Math.Sin(rot), Math.Cos(rot), 0);
		var pts = new[]
		{
			center - ux * hw - uy * hh,
			center + ux * hw - uy * hh,
			center + ux * hw + uy * hh,
			center - ux * hw + uy * hh,
		};
		for (int i = 0; i < 4; i++)
		{
			this._block.Entities.Add(dimensionLine(pts[i], pts[(i + 1) % 4], st));
		}
	}

	/// <summary>F2D: DIMTAD에 따라 문자 가운데를 띄운다 (위·JIS = 문자 위쪽, 아래 = 아래쪽, 밖 = 측정점 반대쪽).</summary>
	private XYZ offsetText(XYZ pt, double rot, double off, XYZ up)
	{
		var st = this.ActiveStyle;
		XYZ textUp = new XYZ(-Math.Sin(rot), Math.Cos(rot), 0);
		switch (st.TextVerticalAlignment)
		{
			case DimensionTextVerticalAlignment.Centered:
				return pt;
			case DimensionTextVerticalAlignment.Below:
				return pt - textUp * off;
			case DimensionTextVerticalAlignment.Outside:
				{
					XYZ away = (this.DefinitionPoint - this.measurementOrigin()).Dot(up) < 0 ? -up : up;
					double sgn = textUp.Dot(away) >= 0 ? 1 : -1;
					return pt + textUp * (off * sgn);
				}
			default:
				return pt + textUp * off;
		}
	}

	/// <summary>F2D: 치수 문자 (GetMeasurementText가 문자 재정의 &lt;&gt;까지 처리).</summary>
	protected string radialText()
	{
		string text = this.GetMeasurementText();
		return text;
	}

	/// <summary>F2D: 측정점 (보조선이 시작하는 쪽) — 문자 "밖" 배치 방향 판단용.</summary>
	protected virtual XYZ measurementOrigin() => this.DefinitionPoint;

	/// <summary>F2D: 첫 화살촉 블록 (DIMSAH 켜짐 = DIMBLK1, 아니면 DIMBLK). null = 닫고 채움.</summary>
	public static BlockRecord FirstArrowBlock(DimensionStyle st) => st.SeparateArrowBlocks ? st.DimArrow1 : st.ArrowBlock;

	/// <summary>F2D: 둘째 화살촉 블록 (DIMSAH 켜짐 = DIMBLK2, 아니면 DIMBLK).</summary>
	public static BlockRecord SecondArrowBlock(DimensionStyle st) => st.SeparateArrowBlocks ? st.DimArrow2 : st.ArrowBlock;

	protected static Entity dimensionLine(XYZ start, XYZ end, DimensionStyle style)
	{
		return new Line(start, end)
		{
			Color = style.DimensionLineColor,
			LineType = style.LineType ?? LineType.ByLayer,
			LineWeight = style.DimensionLineWeight
		};
	}

	protected static Line extensionLine(XYZ start, XYZ end, DimensionStyle style, LineType linetype)
	{
		return new Line(start, end)
		{
			Color = style.ExtensionLineColor,
			LineType = linetype ?? LineType.ByLayer,
			LineWeight = style.ExtensionLineWeight
		};
	}

	/// <summary>
	/// F2D: 반지름·지름 치수의 바깥 수평 문자 (DIMTOH 켜짐) — 기존 CAD 블록과 같은 모양:
	/// 원 위 화살촉 → 화살촉 밑변에서 바깥으로 화살촉 길이만큼 지시선 → 꺾인 점에서 수평 받침선(간격 + 문자 폭) → 그 위에 문자.
	/// 사용자가 옮긴 문자는 저장된 점이 받침선 높이·문자 가운데이고, 꺾인 점은 지시선 연장이 그 높이를 만나는 곳.
	/// 처리했으면 true (치수선·화살촉·문자를 모두 넣음).
	/// </summary>
	protected bool radialLanding(XY center, XY ref1, double angleRef, short inside, string text)
	{
		var st = this.ActiveStyle;
		if (inside > 0 || !st.TextOutsideHorizontal || Math.Abs(this.TextRotation) > 1e-12)
		{
			return false;
		}
		double scale = st.ScaleFactor > 0 ? st.ScaleFactor : 1.0;
		double asz = st.ArrowSize * scale;
		double gap = Math.Abs(st.DimensionLineGap) * scale;
		XY u = (ref1 - center);
		double ul = u.GetLength();
		if (ul < 1e-12)
		{
			return false;
		}
		u = u / ul;
		var arrowBlock = SecondArrowBlock(st);
		XY baseP = arrowBlock == null ? ref1 + u * asz : ref1;

		MText mText = this.createTextEntity(XYZ.Zero, text);
		var (tw, th) = this.measureDimText(mText);

		XY knee;
		XY textCenter;
		double side;
		double farEdge;
		if (this.IsTextUserDefinedLocation)
		{
			XY p = this.TextMiddlePoint.Convert<XY>();
			double t = Math.Abs(u.Y) > 1e-6 ? (p.Y - baseP.Y) / u.Y : asz;
			if (t < 0) t = asz;
			knee = baseP + u * t;
			side = p.X >= knee.X ? 1 : -1;
			textCenter = new XY(p.X, knee.Y + gap + th / 2);
			farEdge = p.X + side * tw / 2;
		}
		else
		{
			knee = baseP + u * asz;
			side = u.X >= 0 ? 1 : -1;
			textCenter = new XY(knee.X + side * (gap + tw / 2), knee.Y + gap + th / 2);
			farEdge = knee.X + side * (gap + tw);
			this.TextMiddlePoint = textCenter.Convert<XYZ>();
		}

		if (!st.SuppressFirstDimensionLine || !st.SuppressSecondDimensionLine)
		{
			if ((knee - baseP).GetLength() > 1e-9)
			{
				this._block.Entities.Add(dimensionLine(baseP.Convert<XYZ>(), knee.Convert<XYZ>(), st));
			}
			this._block.Entities.Add(dimensionLine(knee.Convert<XYZ>(), new XYZ(farEdge, knee.Y, 0), st));
			this._block.Entities.Add(this.radialArrow(ref1, angleRef, inside));
		}

		mText.InsertPoint = textCenter.Convert<XYZ>();
		mText.AttachmentPoint = AttachmentPointType.MiddleCenter;
		mText.AlignmentPoint = XYZ.AxisX;
		this._block.Entities.Add(mText);
		return true;
	}

	protected void angularBlock(double radius, XY centerRef, XY ref1, double minOffset, bool drawRef2)
	{
		//Common for Diameter and radial
		double offset = this.DefinitionPoint.DistanceFrom(this.TextMiddlePoint);
		XY defPoint = this.DefinitionPoint.Convert<XY>();
		double angleRef = centerRef.GetAngle(ref1);

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

		XY dimRef = XY.Polar(centerRef, offset - this.ActiveStyle.DimensionLineGap * this.ActiveStyle.ScaleFactor, angleRef);

		// reference points
		Layer defPoints = Layer.Defpoints;
		this._block.Entities.Add(new Point(ref1.Convert<XYZ>()) { Layer = defPoints });

		// F2D: 바깥 수평 문자는 기존 CAD처럼 지시선 + 받침선
		if (this.radialLanding(centerRef, ref1, angleRef, inside, this.radialText()))
		{
			if (this.ActiveStyle.TextOutsideExtensions)
			{
				this._block.Entities.Add(new Line(defPoint, ref1)
				{
					Color = this.ActiveStyle.DimensionLineColor,
					LineType = this.ActiveStyle.LineType ?? LineType.ByLayer,
					LineWeight = this.ActiveStyle.DimensionLineWeight
				});
			}
			if (!this.ActiveStyle.CenterMarkSize.IsZero())
			{
				this._block.Entities.AddRange(centerCross(centerRef.Convert<XYZ>(), radius, this.ActiveStyle));
			}
			return;
		}

		// dimension lines
		if (!this.ActiveStyle.SuppressFirstDimensionLine && !this.ActiveStyle.SuppressSecondDimensionLine)
		{
			if (inside > 0)
			{
				this._block.Entities.Add(dimensionRadialLine(dimRef, ref1, angleRef, inside));
				// F2D: 화살촉 (원 위 점, 안쪽 문자면 바깥으로)
				this._block.Entities.Add(this.radialArrow(ref1, angleRef, inside));
			}
			else
			{
				// F2D: 원 안쪽 치수선은 DIMTOFL이 켜졌을 때만 (기존 CAD와 같음)
				if (this.ActiveStyle.TextOutsideExtensions)
				{
					this._block.Entities.Add(new Line(defPoint, ref1)
					{
						Color = this.ActiveStyle.DimensionLineColor,
						LineType = this.ActiveStyle.LineType ?? LineType.ByLayer,
						LineWeight = this.ActiveStyle.DimensionLineWeight
					});
				}
				this._block.Entities.Add(dimensionRadialLine(dimRef, ref1, angleRef, inside));
				this._block.Entities.Add(this.radialArrow(ref1, angleRef, inside));

				if (drawRef2)
				{
					XY dimRef2 = XY.Polar(centerRef, radius + minOffset - this.ActiveStyle.DimensionLineGap * this.ActiveStyle.ScaleFactor, Math.PI + angleRef);
					this._block.Entities.Add(dimensionRadialLine(dimRef2, defPoint, Math.PI + angleRef, inside));
					//End Arrow
				}
			}
		}

		// center cross
		if (!this.ActiveStyle.CenterMarkSize.IsZero())
		{
			this._block.Entities.AddRange(centerCross(centerRef.Convert<XYZ>(), radius, this.ActiveStyle));
		}

		// dimension text
		string text = this.GetMeasurementText();

		double textRot = angleRef;
		short reverse = 1;
		if (textRot > MathHelper.HalfPI && textRot <= MathHelper.ThreeHalfPI)
		{
			textRot += Math.PI;
			reverse = -1;
		}

		if (!this.IsTextUserDefinedLocation)
		{
			XY textPos = XY.Polar(dimRef, -reverse * inside * this.ActiveStyle.DimensionLineGap * this.ActiveStyle.ScaleFactor, textRot);
			this.TextMiddlePoint = textPos.Convert<XYZ>();
		}

		AttachmentPointType attachmentPoint = reverse * inside < 0 ? AttachmentPointType.MiddleLeft : AttachmentPointType.MiddleRight;
		MText mText = createTextEntity(this.TextMiddlePoint, text);
		mText.AttachmentPoint = attachmentPoint;

		this._block.Entities.Add(mText);
	}

	protected List<Entity> centerCross(XYZ center, double radius, DimensionStyle style)
	{
		List<Entity> lines = new();
		if (style.CenterMarkSize.IsZero())
		{
			return lines;
		}

		XYZ c1;
		XYZ c2;
		double dist = Math.Abs(style.CenterMarkSize * style.ScaleFactor);

		// center mark
		c1 = new XYZ(0.0, -dist, 0) + center;
		c2 = new XYZ(0.0, dist, 0) + center;
		lines.Add(new Line(c1, c2) { Color = style.ExtensionLineColor, LineWeight = style.ExtensionLineWeight });
		c1 = new XYZ(-dist, 0.0, 0) + center;
		c2 = new XYZ(dist, 0.0, 0) + center;
		lines.Add(new Line(c1, c2) { Color = style.ExtensionLineColor, LineWeight = style.ExtensionLineWeight });

		// center lines
		if (style.CenterMarkSize < 0)
		{
			c1 = new XYZ(2 * dist, 0.0, 0) + center;
			c2 = new XYZ(radius + dist, 0.0, 0) + center;
			lines.Add(new Line(c1, c2) { Color = style.ExtensionLineColor, LineWeight = style.ExtensionLineWeight });

			c1 = new XYZ(-2 * dist, 0.0, 0) + center;
			c2 = new XYZ(-radius - dist, 0.0, 0) + center;
			lines.Add(new Line(c1, c2) { Color = style.ExtensionLineColor, LineWeight = style.ExtensionLineWeight });

			c1 = new XYZ(0.0, 2 * dist, 0) + center;
			c2 = new XYZ(0.0, radius + dist, 0) + center;
			lines.Add(new Line(c1, c2) { Color = style.ExtensionLineColor, LineWeight = style.ExtensionLineWeight });

			c1 = new XYZ(0.0, -2 * dist, 0) + center;
			c2 = new XYZ(0.0, -radius - dist, 0) + center;
			lines.Add(new Line(c1, c2) { Color = style.ExtensionLineColor, LineWeight = style.ExtensionLineWeight });
		}
		return lines;
	}

	protected void createBlock()
	{
		this._activeStyle = null;
		this._activeStyle = this.GetActiveDimensionStyle();

		if (this._block == null)
		{
			this._block = new BlockRecord(this.generateBlockName());
			this._block.IsAnonymous = true;
		}

		if (this.Document != null)
		{
			this._block = this.updateTableEntry(this._block, b => this._block = b, this.Document.BlockRecords);
		}

		this._block.Entities.Clear();
	}

	/// <summary>
	/// F2D: 각도 치수 블록 (2선·3점 공통). c = 꼭짓점, d1·d2 = 두 반직선 방향(단위), t·min/max = 각 반직선 위 원래 객체 범위
	/// (보조선 길이 계산용), arcPoint = 치수 호가 지나는 점. 호 쪽(호 점을 품는 부채꼴)을 재고 호·화살표·보조선·각도 문자를 만든다.
	/// </summary>
	protected void buildAngularBlock(XYZ c, XYZ d1, double t1min, double t1max, XYZ d2, double t2min, double t2max, XYZ arcPoint)
	{
		var style = this.ActiveStyle;
		double scale = style.ScaleFactor > 0 ? style.ScaleFactor : 1.0;
		double radius = Math.Sqrt((arcPoint.X - c.X) * (arcPoint.X - c.X) + (arcPoint.Y - c.Y) * (arcPoint.Y - c.Y));
		if (radius < 1e-9 || double.IsNaN(c.X) || double.IsNaN(c.Y))
		{
			return;
		}

		static double Norm(double a)
		{
			a %= 2 * Math.PI;
			return a < 0 ? a + 2 * Math.PI : a;
		}

		double a1 = Math.Atan2(d1.Y, d1.X), a2 = Math.Atan2(d2.Y, d2.X);
		double aa = Math.Atan2(arcPoint.Y - c.Y, arcPoint.X - c.X);
		// 시계 반대 방향으로 시작 → 끝 부채꼴이 호 점을 품도록
		bool firstIsStart = Norm(aa - a1) <= Norm(a2 - a1);
		double s = firstIsStart ? a1 : a2, e = firstIsStart ? a2 : a1;
		double sweep = Norm(e - s);
		double sMin = firstIsStart ? t1min : t2min, sMax = firstIsStart ? t1max : t2max;
		double eMin = firstIsStart ? t2min : t1min, eMax = firstIsStart ? t2max : t1max;

		XYZ At(double ang, double r) => new XYZ(c.X + r * Math.Cos(ang), c.Y + r * Math.Sin(ang), c.Z);

		// 정의점
		this._block.Entities.Add(this.createDefinitionPoint(At(s, sMax > 0 ? sMax : radius)));
		this._block.Entities.Add(this.createDefinitionPoint(At(e, eMax > 0 ? eMax : radius)));

		// 치수 호 (문자가 호 가운데에 걸치면 아래에서 둘로 나눈다)
		Arc dimArc = new Arc
		{
			Center = c,
			Radius = radius,
			StartAngle = Norm(s),
			EndAngle = Norm(e),
			Color = style.DimensionLineColor,
			LineType = style.LineType ?? LineType.ByLayer,
			LineWeight = style.DimensionLineWeight,
		};
		this._block.Entities.Add(dimArc);

		// 화살표: 호 양 끝에서 바깥쪽(접선 방향)
		this._block.Entities.Add(this.dimensionArrow(At(s, radius), new XYZ(Math.Sin(s), -Math.Cos(s), 0), style, FirstArrowBlock(style)));
		this._block.Entities.Add(this.dimensionArrow(At(e, radius), new XYZ(-Math.Sin(e), Math.Cos(e), 0), style, SecondArrowBlock(style)));

		// 보조선: 호가 원래 선의 범위 밖이면 선 끝에서 호까지
		double exo = style.ExtensionLineOffset * scale, exe = style.ExtensionLineExtension * scale;
		void Ext(double ang, double tmin, double tmax, LineType lt)
		{
			if (!style.SuppressFirstExtensionLine || !style.SuppressSecondExtensionLine)
			{
				if (radius > tmax + exo)
				{
					this._block.Entities.Add(extensionLine(At(ang, tmax + exo), At(ang, radius + exe), style, lt));
				}
				else if (radius < tmin - exo)
				{
					this._block.Entities.Add(extensionLine(At(ang, tmin - exo), At(ang, radius - exe), style, lt));
				}
			}
		}
		if (!style.SuppressFirstExtensionLine) Ext(s, sMin, sMax, style.LineTypeExt1);
		if (!style.SuppressSecondExtensionLine) Ext(e, eMin, eMax, style.LineTypeExt2);

		// 각도 문자: 호 가운데 바깥쪽, 호를 따라 읽기 쉬운 방향
		string text = this.GetMeasurementText();
		double h = style.TextHeight * scale;
		double gap = style.DimensionLineGap * scale;
		double mid = s + sweep / 2;
		// F2D: 문자 위치 (기존 CAD 규칙)
		//  - 문자를 수평으로(DIMTIH) 쓰고 그 자리의 치수선이 수평에 가까우면: DIMTAD "위"는 치수선 위쪽(도면 +Y)에 띄움
		//  - 문자는 수평인데 치수선이 기울었거나 DIMTAD "중앙"이면: 문자를 호 위 가운데에 두고 그만큼 호를 끊음
		//  - 문자를 치수선 방향으로 쓰면: "위"는 호 바깥쪽에 띄움
		bool horizontalText = style.TextInsideHorizontal && Math.Abs(this.TextRotation) < 1e-12;
		bool lineHorizontal = Math.Abs(Math.Sin(mid)) > Math.Cos(15 * Math.PI / 180);
		bool centered = style.TextVerticalAlignment == DimensionTextVerticalAlignment.Centered || (horizontalText && !lineHorizontal);
		if (!this.IsTextUserDefinedLocation)
		{
			if (centered)
			{
				this.TextMiddlePoint = At(mid, radius);
			}
			else if (horizontalText)
			{
				XYZ onArc = At(mid, radius);
				this.TextMiddlePoint = new XYZ(onArc.X, onArc.Y + gap + h / 2, onArc.Z);
			}
			else
			{
				this.TextMiddlePoint = At(mid, radius + gap + h / 2);
			}
		}
		if (centered && !this.IsTextUserDefinedLocation)
		{
			// 문자 상자(대략 글자 수 × 0.7h × h)가 호 접선 방향으로 차지하는 절반 길이 + 간격
			double w = Math.Max(text.Length, 1) * h * 0.7;
			double tx = -Math.Sin(mid), ty = Math.Cos(mid);
			double half = horizontalText ? Math.Abs(tx) * w / 2 + Math.Abs(ty) * h / 2 : w / 2;
			double ga = (half + gap) / radius;
			if (2 * ga < sweep)
			{
				this._block.Entities.Remove(dimArc);
				this._block.Entities.Add(new Arc { Center = c, Radius = radius, StartAngle = Norm(s), EndAngle = Norm(mid - ga), Color = dimArc.Color, LineType = dimArc.LineType, LineWeight = dimArc.LineWeight });
				this._block.Entities.Add(new Arc { Center = c, Radius = radius, StartAngle = Norm(mid + ga), EndAngle = Norm(e), Color = dimArc.Color, LineType = dimArc.LineType, LineWeight = dimArc.LineWeight });
			}
		}
		double rot = Norm(mid - Math.PI / 2);
		if (rot > Math.PI / 2 && rot <= 3 * Math.PI / 2)
		{
			rot += Math.PI;
		}
		// F2D: 치수 스타일의 "문자 수평"(DIMTIH)이면 수평, 문자 각도를 따로 주면 그 각도
		if (style.TextInsideHorizontal)
		{
			rot = 0;
		}
		if (Math.Abs(this.TextRotation) > 1e-12)
		{
			rot = this.TextRotation;
		}
		MText mText = this.createTextEntity(this.TextMiddlePoint, text);
		mText.Height = h;
		mText.AlignmentPoint = new XYZ(Math.Cos(rot), Math.Sin(rot), 0);
		mText.Color = style.TextColor;
		if (style.Style != null)
		{
			mText.Style = style.Style;
		}
		this._block.Entities.Add(mText);
	}

	protected Point createDefinitionPoint(XYZ location)
	{
		return new Point(location) { Layer = Layer.Defpoints };
	}

	protected MText createTextEntity(XYZ insertPoint, string text)
	{
		// F2D: 공차·한계 쌓기는 DIMTOLJ 세로 정렬을 앞에 (기존 CAD 블록 "\A1;30.0\S+0.1^-0.2;" — 없으면 기존 CAD가 위 칸을 띄워 그림)
		if (text.Contains("\\S") && !text.StartsWith("\\A", StringComparison.Ordinal))
		{
			text = $"\\A{(int)this.ActiveStyle.ToleranceAlignment};{text}";
		}
		MText mText = new MText()
		{
			Value = text,
			AttachmentPoint = AttachmentPointType.MiddleCenter,
			InsertPoint = insertPoint,
			Height = this.ActiveStyle.TextHeight,
			// F2D: 치수 문자 색(DIMCLRT)·문자 스타일 (예전에는 ByLayer라 스타일의 노란색 대신 도면층 색으로 보였다)
			Color = this.ActiveStyle.TextColor,
		};
		if (this.ActiveStyle.Style != null)
		{
			mText.Style = this.ActiveStyle.Style;
		}

		return mText;
	}

	/// <summary>F2D: 반지름·지름 치수 화살촉. inside &gt; 0(문자가 원 안)이면 바깥쪽, 아니면 중심 쪽을 가리킨다.</summary>
	protected Entity radialArrow(XY tip, double angleRef, short inside)
	{
		double sign = inside > 0 ? 1 : -1;
		var dir = new XYZ(Math.Cos(angleRef) * sign, Math.Sin(angleRef) * sign, 0);
		return this.dimensionArrow(tip.Convert<XYZ>(), dir, this.ActiveStyle, SecondArrowBlock(this.ActiveStyle));
	}

	protected Entity dimensionArrow(XYZ insertPoint, XYZ dir, DimensionStyle style, BlockRecord record)
	{
		double scale = style.ArrowSize * style.ScaleFactor;
		double rotation = Math.Atan2(dir.Y, dir.X);

		if (record == null)
		{
			XYZ p = XYZ.Cross(this.Normal, dir).Normalize();

			Solid arrow = new Solid();
			arrow.FirstCorner = insertPoint;
			arrow.SecondCorner = insertPoint - scale * dir - scale / 6 * p;
			arrow.ThirdCorner = insertPoint - scale * dir + scale / 6 * p;
			arrow.FourthCorner = arrow.ThirdCorner; // F2D: 기본 화살촉도 치수선 색 (전에는 ByBlock이라 치수선과 색이 달랐다)
			arrow.Color = style.DimensionLineColor;

			return arrow;
		}
		else
		{
			Insert arrow = new Insert(record)
			{
				InsertPoint = insertPoint,
				Color = style.DimensionLineColor,
				XScale = scale,
				YScale = scale,
				ZScale = scale,
				Rotation = rotation,
				LineWeight = style.DimensionLineWeight,
				Normal = this.Normal,
			};
			return arrow;
		}
	}

	protected Line dimensionRadialLine(XY start, XY end, double rotation, short reversed)
	{
		var style = this.ActiveStyle;
		double ext = -style.ArrowSize * style.ScaleFactor;

		end = XY.Polar(end, reversed * ext, rotation);

		return new Line(start, end)
		{
			Color = style.DimensionLineColor,
			LineType = style.LineType ?? LineType.ByLayer,
			LineWeight = style.DimensionLineWeight
		};
	}

	private string generateBlockName()
	{
		return $"*D{this.Handle}";
	}
}