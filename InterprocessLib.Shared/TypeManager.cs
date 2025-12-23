using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib;

internal class TypeManager
{
	private bool _initializedCoreTypes = false;

	private static readonly MethodInfo _registerTypeMethod = typeof(TypeManager).GetMethod(nameof(RegisterType), BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new MissingMethodException(nameof(RegisterType));

	private readonly List<Type> _newTypes = new();

	private static List<Type> CurrentRendererCommandTypes => (List<Type>)typeof(PolymorphicMemoryPackableEntity<RendererCommand>).GetField("types", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!  ?? throw new MissingFieldException("types");

	private readonly List<Func<IMemoryPackable>> _borrowers = new();

	private readonly List<Action<IMemoryPackable>> _returners = new();

	private readonly Dictionary<Type, int> _typeToIndex = new();

	private IMemoryPackerEntityPool _pool;

	private static readonly MethodInfo _borrowMethod = typeof(TypeManager).GetMethod(nameof(Borrow), BindingFlags.Instance | BindingFlags.NonPublic, null, [], null) ?? throw new MissingMethodException(nameof(Borrow));
	private static readonly MethodInfo _returnMethod = typeof(TypeManager).GetMethod(nameof(Return), BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(IMemoryPackable)], null) ?? throw new MissingMethodException(nameof(Return));

	// These are types that will be assumed to be already registered in the other process
	private static readonly List<Type> _coreTypes =
	[
		typeof(MessengerReadyCommand),
		typeof(TypeRegistrationCommand),
		typeof(EmptyCommand),
		typeof(StringCommand),
		typeof(StringArrayCommand),
		typeof(TypeCommand),
		typeof(PingCommand),
	];

	private Action<Type>? _onRegisteredCallback;

	static TypeManager()
	{
		// Trigger RendererCommand static constructor
		new WrapperCommand();

		var list = new List<Type>();
		list.AddRange(CurrentRendererCommandTypes);
		var wrapperType = typeof(WrapperCommand);
		if (!list.Contains(wrapperType))
			list.Add(wrapperType);

		WrapperCommand.InitNewTypes(list);
	}

	internal TypeManager(IMemoryPackerEntityPool pool, Action<Type>? onRegisteredCallback)
	{
		_pool = pool;
		_onRegisteredCallback = onRegisteredCallback;
		InitializeCoreTypes();
	}

	internal void InitializeCoreTypes()
	{
		if (_initializedCoreTypes) return;

		PushNewTypes(_coreTypes);

		_initializedCoreTypes = true;
	}

	internal Type GetTypeFromIndex(int index)
	{
		return _newTypes[index];
	}

	internal int GetTypeIndex(Type type)
	{
		return _typeToIndex[type];
	}

	internal bool IsTypeRegistered<T>() where T : class, IMemoryPackable, new()
	{
		return _typeToIndex.ContainsKey(typeof(T));
	}

	internal void InvokeRegisterType(Type type)
	{
		_registerTypeMethod!.MakeGenericMethod(type).Invoke(this, null);
	}

	internal void RegisterType<T>() where T : class, IMemoryPackable, new()
	{
		var type = typeof(T);

		PushNewTypes([type]);
	}

	private IMemoryPackable? Borrow<T>() where T : class, IMemoryPackable, new()
	{
		return _pool.Borrow<T>();
	}

	private void Return<T>(IMemoryPackable obj) where T : class, IMemoryPackable, new()
	{
		_pool.Return((T)obj);
	}

	internal IMemoryPackable Borrow(Type type)
	{
		return _borrowers[_typeToIndex[type]]();
	}

	internal void Return(Type type, IMemoryPackable obj)
	{
		_returners[_typeToIndex[type]](obj);
	}

	private void PushNewTypes(List<Type> types)
	{
		foreach (var type in types)
		{
			_newTypes.Add(type);
			_borrowers.Add((Func<IMemoryPackable>)_borrowMethod!.MakeGenericMethod(type).CreateDelegate(typeof(Func<IMemoryPackable>), this));
			_returners.Add((Action<IMemoryPackable>)_returnMethod!.MakeGenericMethod(type).CreateDelegate(typeof(Action<IMemoryPackable>), this));
			_typeToIndex[type] = _newTypes.Count - 1;

			if (!_coreTypes.Contains(type))
				_onRegisteredCallback?.Invoke(type);
		}
	}
}