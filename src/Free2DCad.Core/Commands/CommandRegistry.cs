namespace Free2DCad.Core.Commands;

/// <summary>
/// CAD 명령어 및 별칭을 관리하고 생성 함수를 제공하는 레지스트리. 대소문자를 구분하지 않는다.
/// </summary>
public static class CommandRegistry
{
	private static readonly Dictionary<string, Func<EditorCommand>> Factories = new(StringComparer.OrdinalIgnoreCase);

	static CommandRegistry()
	{
		Register(() => new LineCommand());
		Register(() => new RectangCommand());
		Register(() => new CircleCommand());
		Register(() => new ArcCommand());
		Register(() => new PlineCommand());
		Register(() => new TextCommand());
		Register(() => new EraseCommand());
		Register(() => new MoveCommand());
		Register(() => new CopyCommand());
		Register(() => new RotateCommand());
		Register(() => new ScaleCommand());
		Register(() => new MirrorCommand());
		Register(() => new OffsetCommand());
		Register(() => new ExplodeCommand());
		Register(() => new DistCommand());
		Register(() => new DimLinearCommand());
		Register(() => new DimLinearCommand(true));
		Register(() => new TrimCommand());
		Register(() => new TableOutCommand());
		Register(() => new CopyClipCommand());
		Register(() => new PasteClipCommand());
		Register(() => new ExtendCommand());
		Register(() => new FilletCommand());
		Register(() => new ChamferCommand());
		Register(() => new BreakCommand());
		Register(() => new JoinCommand());
		Register(() => new StretchCommand());
		Register(() => new ArrayCommand());
		Register(() => new MTextCommand());
		Register(() => new DimRadiusCommand());
		Register(() => new DimDiameterCommand());
		Register(() => new DimAngularCommand());
		Register(() => new HatchCommand());
		Register(() => new MatchPropCommand());
		Register(() => new ListCommand());
		Register(() => new IdCommand());
		Register(() => new AreaCommand());
		Register(() => new ZoomCommand());
		Register(() => new PanCommand());
		Register(() => new UndoCommand());
		Register(() => new RedoCommand());
		Register(() => new LayerCommand());
		Register(() => new LayoutCommand());
		Register(() => new BlockCommand());
		Register(() => new InsertCommand());
		Register(() => new TableCommand());
		Register(() => new TableExportCommand());
		Register(() => new PolygonCommand());
		Register(() => new EllipseCommand());
		Register(() => new SplineCommand());
		Register(() => new PointCommand());
		Register(() => new DivideMeasureCommand(false));
		Register(() => new DivideMeasureCommand(true));
		Register(() => new XLineCommand(false));
		Register(() => new XLineCommand(true));
		Register(() => new DonutCommand());
		Register(() => new RevCloudCommand());
		Register(() => new SolidCommand());
		Register(() => new LengthenCommand());
		Register(() => new BreakAtPointCommand());
		Register(() => new ReverseCommand());
		Register(() => new AlignCommand());
		Register(() => new PeditCommand());
		Register(() => new DrawOrderCommand());
		Register(() => new HatchToBackCommand());
		Register(() => new TextToFrontCommand());
		Register(() => new SetByLayerCommand());
		Register(() => new ArrayCommand(ArrayCommand.ArrayKind.Rect));
		Register(() => new ArrayCommand(ArrayCommand.ArrayKind.Polar));
		Register(() => new ArrayCommand(ArrayCommand.ArrayKind.Path));
		Register(() => new LayerPickCommand(LayerPickCommand.Op.MakeCurrent));
		Register(() => new LayerPickCommand(LayerPickCommand.Op.Off));
		Register(() => new LayerPickCommand(LayerPickCommand.Op.Freeze));
		Register(() => new LayerPickCommand(LayerPickCommand.Op.Lock));
		Register(() => new LayerPickCommand(LayerPickCommand.Op.Unlock));
		Register(() => new LayerPickCommand(LayerPickCommand.Op.Isolate));
		Register(() => new LayerPickCommand(LayerPickCommand.Op.ChangeToCurrent));
		Register(() => new LayerPickCommand(LayerPickCommand.Op.Match));
		Register(() => new LayerPickCommand(LayerPickCommand.Op.Delete));
		Register(() => new LayerAllCommand(LayerAllCommand.Op.AllOn));
		Register(() => new LayerAllCommand(LayerAllCommand.Op.ThawAll));
		Register(() => new LayerAllCommand(LayerAllCommand.Op.Unisolate));
		Register(() => new LayerAllCommand(LayerAllCommand.Op.Previous));
		Register(() => new DimChainCommand(false));
		Register(() => new DimChainCommand(true));
		Register(() => new DimOrdinateCommand());
		Register(() => new DimArcCommand());
		Register(() => new DimCenterCommand());
		Register(() => new DimRegenCommand());
		Register(() => new ClassicLeaderCommand());
		Register(() => new LeaderCommand("QLEADER"));
		Register(() => new MLeaderCommand());
		Register(() => new QDimCommand());
		Register(() => new Txt2MtxtCommand());
		Register(() => new TextEditCommand());
		Register(() => new BoundaryCommand());
		Register(() => new AttDefCommand());
		Register(() => new AttSyncCommand());
		Register(() => new AttDispCommand());
		Register(() => new BaseCommand());
		Register(() => new AttEditCommand("EATTEDIT"));
		Register(() => new AttEditCommand("-ATTEDIT"));
		Register(() => new GroupCommand(false));
		Register(() => new GroupCommand(true));
		Register(() => new UngroupCommand());
		Register(() => new AddSelectedCommand());
		Register(() => new SelectSimilarCommand());
		Register(() => new DimEditCommand());
		Register(() => new DimTEditCommand());
		Register(() => new ScaleTextCommand());
		Register(() => new TotalLengthCommand());
		Register(() => new StatusCommand());
		Register(() => new CenterMarkCommand());
		Register(() => new CenterLineCommand());
		Register(() => new DimSpaceCommand());
		Register(() => new WblockCommand());
		Register(() => new MLineCommand());
		Register(() => new WipeoutCommand());
		Register(() => new SplineEditCommand());
		Register(() => new DimJoggedCommand());
		Register(() => new ArrayEditCommand());
		Register(() => new CutClipCommand());
		Register(() => new LayerStateCommand(false));
		Register(() => new LayerStateCommand(true));
	}

	public static void Register(Func<EditorCommand> factory)
	{
		ArgumentNullException.ThrowIfNull(factory);

		var instance = factory();
		Factories[instance.Name] = factory;
		foreach (var alias in instance.Aliases)
		{
			Factories[alias] = factory;
		}
	}

	public static EditorCommand? Create(string nameOrAlias)
	{
		if (string.IsNullOrWhiteSpace(nameOrAlias))
		{
			return null;
		}

		if (Factories.TryGetValue(nameOrAlias.Trim(), out var factory))
		{
			return factory();
		}

		return null;
	}

	/// <summary>등록된 명령 목록 (명령 이름, 별칭). 명령줄 자동완성용.</summary>
	public static IReadOnlyList<(string Name, string[] Aliases)> All =>
		Factories.Values.Distinct()
			.Select(f => f())
			.Select(c => (c.Name, c.Aliases.ToArray()))
			.GroupBy(c => c.Name)
			.Select(g => g.First())
			.OrderBy(c => c.Name)
			.ToList();

	public static bool IsRegistered(string nameOrAlias)
	{
		return !string.IsNullOrWhiteSpace(nameOrAlias) && Factories.ContainsKey(nameOrAlias.Trim());
	}
}
