using Renderite.Shared;
using System.Collections.Generic;
using System.Reflection;

namespace InterprocessLib;

internal class TypeManager
{
	private readonly HashSet<Type> _registeredObjectTypes = new();

	private readonly HashSet<Type> _registeredValueTypes = new();

	private bool _initializedCoreTypes = false;

	private static MethodInfo? _registerValueTypeMethod = typeof(TypeManager).GetMethod(nameof(TypeManager.RegisterAdditionalValueType), BindingFlags.NonPublic | BindingFlags.Instance);

	private static MethodInfo? _registerObjectTypeMethod = typeof(TypeManager).GetMethod(nameof(TypeManager.RegisterAdditionalObjectType), BindingFlags.NonPublic | BindingFlags.Instance);

	private List<Type> _newTypes = new();

	private static List<Type> CurrentRendererCommandTypes => (List<Type>)typeof(PolymorphicMemoryPackableEntity<RendererCommand>).GetField("types", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

	private static Dictionary<string, TypeManager> _typeManagers = new();

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
		// Trigger RendererCommand static constructor
		new WrapperCommand();

		var list = new List<Type>();
		list.AddRange(CurrentRendererCommandTypes);
		list.Add(typeof(WrapperCommand));

		WrapperCommand.InitNewTypes(list);
	}

	internal TypeManager(string queueName)
	{
		_typeManagers.Add(queueName, this);
		InitializeCoreTypes();
	}

	internal static TypeManager GetTypeManager(string queueName)
	{
		return _typeManagers[queueName];
	}

	internal void InitializeCoreTypes()
	{
		if (_initializedCoreTypes) return;

		RegisterAdditionalObjectType<MessengerReadyCommand>();
		RegisterAdditionalObjectType<IdentifiableCommand>();
		RegisterAdditionalObjectType<StringCommand>();
		RegisterAdditionalObjectType<StringListCommand>();

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
		return _newTypes.IndexOf(type);
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

	internal bool IsObjectTypeInitialized<T>() where T : class, IMemoryPackable, new()
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

		var valueListCommandType = typeof(ValueCollectionCommand<,>).MakeGenericType(typeof(List<T>), type);

		var valueHashSetCommandType = typeof(ValueCollectionCommand<,>).MakeGenericType(typeof(HashSet<T>), type);

		_newTypes.AddRange([valueCommandType, valueListCommandType, valueHashSetCommandType]);

		_registeredValueTypes.Add(type);
	}

	private void RegisterAdditionalObjectType<T>() where T : class, IMemoryPackable, new()
	{
		var type = typeof(T);

		if (_registeredObjectTypes.Contains(type))
			throw new InvalidOperationException($"Type {type.Name} is already registered!");

		if (type.ContainsGenericParameters)
			throw new ArgumentException($"Type must be a concrete type!");

		if (type.IsSubclassOf(typeof(PolymorphicMemoryPackableEntity<IdentifiableCommand>)))
		{
			_newTypes.Add(type);
		}

		var objectCommandType = typeof(ObjectCommand<>).MakeGenericType(type);

		var objectListCommandType = typeof(ObjectListCommand<>).MakeGenericType(type);

		_newTypes.AddRange([objectCommandType, objectListCommandType]);

		_registeredObjectTypes.Add(type);
	}
}