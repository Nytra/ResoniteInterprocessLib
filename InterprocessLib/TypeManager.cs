using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib;

internal static class TypeManager
{
	private static readonly HashSet<Type> _registeredObjectTypes = new();

	private static readonly HashSet<Type> _registeredValueTypes = new();

	private static bool _initializedCoreTypes = false;

	internal static MethodInfo? RegisterValueTypeMethod = typeof(TypeManager).GetMethod(nameof(TypeManager.RegisterAdditionalValueType), BindingFlags.NonPublic | BindingFlags.Static);

	internal static MethodInfo? RegisterObjectTypeMethod = typeof(TypeManager).GetMethod(nameof(TypeManager.RegisterAdditionalObjectType), BindingFlags.NonPublic | BindingFlags.Static);

	internal static List<Type> NewTypes = new();

	private static List<Type> RegisteredTypesList => (List<Type>)typeof(PolymorphicMemoryPackableEntity<RendererCommand>).GetField("types", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

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
				RegisterValueTypeMethod!.MakeGenericMethod(valueType).Invoke(null, null);
			}
			catch (Exception ex)
			{
				Messenger.OnWarning?.Invoke($"Could not register additional value type {valueType.Name}!\n{ex}");
			}
		}

		_initializedCoreTypes = true;
	}

	private static void PushNewTypes()
	{
		// Trigger RendererCommand static constructor
		var cmd = new EmptyCommand();

		var list = new List<Type>();
		list.AddRange(RegisteredTypesList);
		foreach (var type in TypeManager.NewTypes)
		{
			if (!list.Contains(type))
				list.Add(type);
		}
		IdentifiableCommand.InitNewTypes(list);
	}

	internal static void InitValueTypeList(List<Type> types)
	{
		foreach (var type in types)
		{
			RegisterValueTypeMethod!.MakeGenericMethod(type).Invoke(null, null);
		}
	}

	internal static void InitObjectTypeList(List<Type> types)
	{
		foreach (var type in types)
		{
			RegisterObjectTypeMethod!.MakeGenericMethod(type).Invoke(null, null);
		}
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

	internal static void RegisterAdditionalValueType<T>() where T : unmanaged
	{
		var type = typeof(T);

		if (_registeredValueTypes.Contains(type))
			throw new InvalidOperationException($"Type {type.Name} is already registered!");

		if (type.ContainsGenericParameters)
			throw new ArgumentException($"Type must be a concrete type!");

		var valueCommandType = typeof(ValueCommand<>).MakeGenericType(type);

		var valueListCommandType = typeof(ValueCollectionCommand<,>).MakeGenericType(typeof(List<T>), type);

		var valueHashSetCommandType = typeof(ValueCollectionCommand<,>).MakeGenericType(typeof(HashSet<T>), type);

		NewTypes.AddRange([valueCommandType, valueListCommandType, valueHashSetCommandType]);

		_registeredValueTypes.Add(type);

		PushNewTypes();
	}

	internal static void RegisterAdditionalObjectType<T>() where T : class, IMemoryPackable, new()
	{
		var type = typeof(T);

		if (_registeredObjectTypes.Contains(type))
			throw new InvalidOperationException($"Type {type.Name} is already registered!");

		if (type.ContainsGenericParameters)
			throw new ArgumentException($"Type must be a concrete type!");

		if (type.IsSubclassOf(typeof(PolymorphicMemoryPackableEntity<RendererCommand>)))
		{
			NewTypes.Add(type);
		}

		var objectCommandType = typeof(ObjectCommand<>).MakeGenericType(type);

		var objectListCommandType = typeof(ObjectListCommand<>).MakeGenericType(type);

		NewTypes.AddRange([objectCommandType, objectListCommandType]);

		_registeredObjectTypes.Add(type);

		PushNewTypes();
	}
}