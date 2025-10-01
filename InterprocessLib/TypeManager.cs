using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib;

internal static class TypeManager
{
	private static readonly HashSet<Type> _registeredObjectTypes = new();

	private static readonly HashSet<Type> _registeredValueTypes = new();

	private static bool _initializedCoreTypes = false;

	internal static MethodInfo? RegisterValueTypeMethod = typeof(TypeManager).GetMethod(nameof(TypeManager.RegisterAdditionalValueType), BindingFlags.NonPublic | BindingFlags.Static);

	internal static MethodInfo? RegisterPackableTypeMethod = typeof(TypeManager).GetMethod(nameof(TypeManager.RegisterAdditionalPackableType), BindingFlags.NonPublic | BindingFlags.Static);

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

		RegisterAdditionalPackableType<MessengerReadyCommand>();
		RegisterAdditionalPackableType<EmptyCommand>();
		RegisterAdditionalPackableType<StringCommand>();

		foreach (var valueType in TypeManager._valueTypes)
		{
			try
			{
				TypeManager.RegisterValueTypeMethod!.MakeGenericMethod(valueType).Invoke(null, null);
			}
			catch (Exception ex)
			{
				Messenger.OnWarning?.Invoke($"Could not register additional value type {valueType.Name}!\n{ex}");
			}
		}
	}

	internal static bool IsValueTypeInitialized<T>() where T : unmanaged
	{
		return _registeredValueTypes.Contains(typeof(T));
	}

	internal static bool IsPackableTypeInitialized<T>() where T : class, IMemoryPackable, new()
	{
		return _registeredObjectTypes.Contains(typeof(T));
	}

	internal static void RegisterAdditionalValueType<T>() where T : unmanaged
	{
		var type = typeof(T);
		Type valueCommandType;

		if (_registeredValueTypes.Contains(type))
			throw new InvalidOperationException($"Type {type.Name} is already registered!");

		if (type.ContainsGenericParameters)
			throw new ArgumentException($"Type must be a concrete type!");

		valueCommandType = typeof(ValueCommand<>).MakeGenericType(type);

		IdentifiableCommand.InitNewTypes([valueCommandType]);

		_registeredValueTypes.Add(type);
	}

	internal static void RegisterAdditionalPackableType<T>() where T : class, IMemoryPackable, new()
	{
		var type = typeof(T);

		if (_registeredObjectTypes.Contains(type))
			throw new InvalidOperationException($"Type {type.Name} is already registered!");

		if (type.ContainsGenericParameters)
			throw new ArgumentException($"Type must be a concrete type!");

		if (type.IsSubclassOf(typeof(PolymorphicMemoryPackableEntity<RendererCommand>)))
		{
			IdentifiableCommand.InitNewTypes([type]);
		}

		var wrapperCommandType = typeof(WrapperCommand<>).MakeGenericType(type);
		IdentifiableCommand.InitNewTypes([wrapperCommandType]);

		_registeredObjectTypes.Add(type);
		_registeredObjectTypes.Add(wrapperCommandType);
	}
}