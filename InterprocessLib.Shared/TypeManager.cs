using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib;

internal static class TypeManager
{
	private static readonly HashSet<Type> _registeredObjectTypes = new();

	private static readonly HashSet<Type> _registeredValueTypes = new();

	private static bool _initializedCoreTypes = false;

	private static MethodInfo? _registerValueTypeMethod = typeof(TypeManager).GetMethod(nameof(TypeManager.RegisterAdditionalValueType), BindingFlags.NonPublic | BindingFlags.Static);

	private static MethodInfo? _registerObjectTypeMethod = typeof(TypeManager).GetMethod(nameof(TypeManager.RegisterAdditionalObjectType), BindingFlags.NonPublic | BindingFlags.Static);

	private static List<Type> _newTypes = new();

	private static List<Type> CurrentRendererCommandTypes => (List<Type>)typeof(PolymorphicMemoryPackableEntity<RendererCommand>).GetField("types", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

	private static Type[] _valueTypes =
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
		InitializeCoreTypes();
	}

	internal static void InitializeCoreTypes()
	{
		if (_initializedCoreTypes) return;

		RegisterAdditionalObjectType<MessengerReadyCommand>();
		RegisterAdditionalObjectType<EmptyCommand>();
		RegisterAdditionalObjectType<StringCommand>();
		RegisterAdditionalObjectType<StringListCommand>();

		foreach (var valueType in TypeManager._valueTypes)
		{
			try
			{
				_registerValueTypeMethod!.MakeGenericMethod(valueType).Invoke(null, null);
			}
			catch (Exception ex)
			{
				Messenger.OnWarning?.Invoke($"Could not register additional value type {valueType.Name}!\n{ex}");
			}
		}

		PushNewTypes();

		_initializedCoreTypes = true;
	}

	private static void PushNewTypes()
	{
		// Trigger RendererCommand static constructor
		new EmptyCommand();

		var list = new List<Type>();
		list.AddRange(CurrentRendererCommandTypes);
		foreach (var type in _newTypes)
		{
			if (!list.Contains(type))
				list.Add(type);
		}

		IdentifiableCommand.InitNewTypes(list);
	}

	internal static void InitValueTypeList(List<Type> types)
	{
		Messenger.OnDebug?.Invoke($"Registering additional value types: {string.Join(",", types.Select(t => t.Name))}");
		foreach (var type in types)
		{
			_registerValueTypeMethod!.MakeGenericMethod(type).Invoke(null, null);
		}
		PushNewTypes();
	}

	internal static void InitObjectTypeList(List<Type> types)
	{
		Messenger.OnDebug?.Invoke($"Registering additional object types: {string.Join(",", types.Select(t => t.Name))}");
		foreach (var type in types)
		{
			_registerObjectTypeMethod!.MakeGenericMethod(type).Invoke(null, null);
		}
		PushNewTypes();
	}

	internal static bool IsValueTypeInitialized<T>() where T : unmanaged
	{
		return _registeredValueTypes.Contains(typeof(T));
	}

	internal static bool IsValueTypeInitialized(Type t)
	{
		return _registeredValueTypes.Contains(t);
	}

	internal static bool IsObjectTypeInitialized<T>() where T : class, IMemoryPackable, new()
	{
		return _registeredObjectTypes.Contains(typeof(T));
	}

	internal static bool IsObjectTypeInitialized(Type t)
	{
		return _registeredObjectTypes.Contains(t);
	}

	private static void RegisterAdditionalValueType<T>() where T : unmanaged
	{
		var type = typeof(T);

		if (_registeredValueTypes.Contains(type))
			throw new InvalidOperationException($"Type {type.Name} is already registered!");

		if (type.ContainsGenericParameters)
			throw new ArgumentException($"Type must be a concrete type!");

		var valueCommandType = typeof(ValueCommand<>).MakeGenericType(type);

		var valueListCommandType = typeof(ValueCollectionCommand<,>).MakeGenericType(typeof(List<T>), type);

		var valueHashSetCommandType = typeof(ValueCollectionCommand<,>).MakeGenericType(typeof(HashSet<T>), type);

		_newTypes.AddRange([valueCommandType, valueListCommandType, valueHashSetCommandType]);

		_registeredValueTypes.Add(type);
	}

	private static void RegisterAdditionalObjectType<T>() where T : class, IMemoryPackable, new()
	{
		var type = typeof(T);

		if (_registeredObjectTypes.Contains(type))
			throw new InvalidOperationException($"Type {type.Name} is already registered!");

		if (type.ContainsGenericParameters)
			throw new ArgumentException($"Type must be a concrete type!");

		if (type.IsSubclassOf(typeof(PolymorphicMemoryPackableEntity<RendererCommand>)))
		{
			_newTypes.Add(type);
		}

		var objectCommandType = typeof(ObjectCommand<>).MakeGenericType(type);

		var objectListCommandType = typeof(ObjectListCommand<>).MakeGenericType(type);

		_newTypes.AddRange([objectCommandType, objectListCommandType]);

		_registeredObjectTypes.Add(type);
	}
}