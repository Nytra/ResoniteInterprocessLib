using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib;

internal class TypeManager
{
	private readonly HashSet<Type> _registeredObjectTypes = new();

	private readonly HashSet<Type> _registeredValueTypes = new();

	private bool _initializedCoreTypes = false;

	private static readonly MethodInfo _registerValueTypeMethod = typeof(TypeManager).GetMethod(nameof(RegisterAdditionalValueType), BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new MissingMethodException(nameof(RegisterAdditionalValueType));

	private static readonly MethodInfo _registerObjectTypeMethod = typeof(TypeManager).GetMethod(nameof(RegisterAdditionalObjectType), BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new MissingMethodException(nameof(RegisterAdditionalObjectType));

	private readonly List<Type> _newTypes = new();

	private static List<Type> CurrentRendererCommandTypes => (List<Type>)typeof(PolymorphicMemoryPackableEntity<RendererCommand>).GetField("types", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!  ?? throw new MissingFieldException("types");

	private readonly List<Func<IMemoryPackable>> _borrowers = new();

	private readonly List<Action<IMemoryPackable>> _returners = new();

	private readonly Dictionary<Type, int> _typeToIndex = new();

	private IMemoryPackerEntityPool _pool;

	private static readonly MethodInfo _borrowMethod = typeof(TypeManager).GetMethod(nameof(Borrow), BindingFlags.Instance | BindingFlags.NonPublic, null, [], null) ?? throw new MissingMethodException(nameof(Borrow));
	private static readonly MethodInfo _returnMethod = typeof(TypeManager).GetMethod(nameof(Return), BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(IMemoryPackable)], null) ?? throw new MissingMethodException(nameof(Return));

	private static readonly Type[] _valueTypes =
	{
		typeof(bool),
		typeof(byte),
		typeof(ushort),
		typeof(uint),
		typeof(ulong),
		typeof(sbyte),
		typeof(short),
		typeof(int),
		typeof(long),
		typeof(float),
		typeof(double),
		typeof(decimal),
		typeof(char),
		typeof(DateTime),
		typeof(TimeSpan)
	};

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

	internal TypeManager(IMemoryPackerEntityPool pool)
	{
		_pool = pool;
		InitializeCoreTypes();
	}

	internal void InitializeCoreTypes()
	{
		if (_initializedCoreTypes) return;

		PushNewTypes([typeof(MessengerReadyCommand), typeof(EmptyCommand), typeof(StringCommand), typeof(StringCollectionCommand<List<string?>>), typeof(StringCollectionCommand<HashSet<string?>>), typeof(StringArrayCommand), typeof(TypeCommand), typeof(PingCommand)]);

		foreach (var valueType in TypeManager._valueTypes)
		{
			try
			{
				_registerValueTypeMethod!.MakeGenericMethod(valueType).Invoke(this, null);
			}
			catch (Exception ex)
			{
				Messenger.OnWarning?.Invoke($"Could not register additional value type {valueType.Name}!\n{ex}");
			}
		}

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

	internal void InitValueTypeList(List<Type> types)
	{
		Messenger.OnDebug?.Invoke($"Registering additional value types: {string.Join(",", types.Select(t => t.Name))}");
		foreach (var type in types)
		{
			_registerValueTypeMethod!.MakeGenericMethod(type).Invoke(this, null);
		}
	}

	internal void InitObjectTypeList(List<Type> types)
	{
		Messenger.OnDebug?.Invoke($"Registering additional object types: {string.Join(",", types.Select(t => t.Name))}");
		foreach (var type in types)
		{
			_registerObjectTypeMethod!.MakeGenericMethod(type).Invoke(this, null);
		}
	}

	internal bool IsValueTypeInitialized<T>() where T : unmanaged
	{
		return _registeredValueTypes.Contains(typeof(T));
	}

	internal bool IsValueTypeInitialized(Type t)
	{
		return _registeredValueTypes.Contains(t);
	}

	internal bool IsObjectTypeInitialized<T>() where T : class?, IMemoryPackable?, new()
	{
		return _registeredObjectTypes.Contains(typeof(T));
	}

	internal bool IsObjectTypeInitialized(Type t)
	{
		return _registeredObjectTypes.Contains(t);
	}

	private void RegisterAdditionalValueType<T>() where T : unmanaged
	{
		var type = typeof(T);

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

	private void RegisterAdditionalObjectType<T>() where T : class, IMemoryPackable, new()
	{
		var type = typeof(T);

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
		}
	}
}