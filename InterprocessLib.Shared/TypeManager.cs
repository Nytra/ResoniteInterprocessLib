using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib;

internal class TypeManager
{
	private readonly HashSet<Type> _registeredObjectTypes = new();

	private readonly HashSet<Type> _registeredValueTypes = new();

	private bool _initializedCoreTypes = false;

	private static readonly MethodInfo _registerDirectCommandTypeMethod = typeof(TypeManager).GetMethod(nameof(RegisterDirectCommandType), BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new MissingMethodException(nameof(RegisterDirectCommandType));

	private readonly List<Type> _newTypes = new();

	private static List<Type> CurrentRendererCommandTypes => (List<Type>)typeof(PolymorphicMemoryPackableEntity<RendererCommand>).GetField("types", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!  ?? throw new MissingFieldException("types");

	private readonly List<Func<IMemoryPackable>> _borrowers = new();

	private readonly List<Action<IMemoryPackable>> _returners = new();

	private readonly Dictionary<Type, int> _typeToIndex = new();

	private IMemoryPackerEntityPool _pool;

	private static readonly MethodInfo _borrowMethod = typeof(TypeManager).GetMethod(nameof(Borrow), BindingFlags.Instance | BindingFlags.NonPublic, null, [], null) ?? throw new MissingMethodException(nameof(Borrow));
	private static readonly MethodInfo _returnMethod = typeof(TypeManager).GetMethod(nameof(Return), BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(IMemoryPackable)], null) ?? throw new MissingMethodException(nameof(Return));

	private static readonly List<Type> _coreTypes =
	[
		typeof(MessengerReadyCommand),
		typeof(TypeRegistrationCommand),
		typeof(EmptyCommand),
		typeof(StringCommand),
		typeof(StringCollectionCommand<List<string?>>),
		typeof(StringCollectionCommand<HashSet<string?>>),
		typeof(StringArrayCommand),
		typeof(TypeCommand),
		typeof(PingCommand),
		typeof(IdentifiableTypeCommand)
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

	internal bool IsValueTypeInitialized<T>() where T : unmanaged
	{
		return _registeredValueTypes.Contains(typeof(T));
	}

	internal bool IsObjectTypeInitialized<T>() where T : class?, IMemoryPackable?, new()
	{
		return _registeredObjectTypes.Contains(typeof(T));
	}

	// internal bool IsDirectCommandTypeInitialized<T>() where T : class?, IMemoryPackable?, new()
	// {
	// 	return _typeToIndex.ContainsKey(typeof(T));
	// }

	internal void RegisterAdditionalValueType<T>() where T : unmanaged
	{
		var type = typeof(T);

		Messenger.OnDebug?.Invoke($"Registering additional value type: {type.Name}");

		if (_registeredValueTypes.Contains(type))
			throw new InvalidOperationException($"Type {type.Name} is already registered!");

		if (type.ContainsGenericParameters)
			throw new ArgumentException($"Type must be a concrete type!");

		var valueCommandType = typeof(ValueCommand<>).MakeGenericType(type);

		var valueArrayCommandType = typeof(ValueArrayCommand<>).MakeGenericType(type);

		var valueListCommandType = typeof(ValueCollectionCommand<,>).MakeGenericType(typeof(List<T>), type);

		var valueHashSetCommandType = typeof(ValueCollectionCommand<,>).MakeGenericType(typeof(HashSet<T>), type);

		PushNewTypes([valueCommandType, valueArrayCommandType, valueListCommandType, valueHashSetCommandType]);

		_registeredValueTypes.Add(type);
	}

	internal void RegisterAdditionalObjectType<T>() where T : class?, IMemoryPackable?, new()
	{
		var type = typeof(T);

		Messenger.OnDebug?.Invoke($"Registering additional object type: {type.Name}");

		if (_registeredObjectTypes.Contains(type))
			throw new InvalidOperationException($"Type {type.Name} is already registered!");

		if (type.ContainsGenericParameters)
			throw new ArgumentException($"Type must be a concrete type!");

		var objectCommandType = typeof(ObjectCommand<>).MakeGenericType(type);

		var objectArrayCommandType = typeof(ObjectArrayCommand<>).MakeGenericType(type);

		var objectListCommandType = typeof(ObjectCollectionCommand<,>).MakeGenericType(typeof(List<T>), type);

		var objectHashSetCommandType = typeof(ObjectCollectionCommand<,>).MakeGenericType(typeof(HashSet<T>), type);

		PushNewTypes([type, objectCommandType, objectArrayCommandType, objectListCommandType, objectHashSetCommandType]);

		_registeredObjectTypes.Add(type);
	}

	internal void InitDirectCommandType(Type type)
	{
		_registerDirectCommandTypeMethod!.MakeGenericMethod(type).Invoke(this, null);
	}

	private void RegisterDirectCommandType<T>() where T : class, IMemoryPackable, new()
	{
		var type = typeof(T);

		Messenger.OnDebug?.Invoke($"Registering direct command type: {type.Name}");

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