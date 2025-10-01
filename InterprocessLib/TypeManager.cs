using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib;

internal static class TypeManager
{
	private static readonly HashSet<Type> _registeredObjectTypes = new();

	private static readonly HashSet<Type> _registeredValueTypes = new();

	internal static bool InitializedCoreTypes = false;

	internal static MethodInfo? _registerValueTypeMethod = typeof(TypeManager).GetMethod(nameof(TypeManager.RegisterAdditionalValueType), BindingFlags.Public | BindingFlags.Static);

	internal static MethodInfo? _registerPackableTypeMethod = typeof(TypeManager).GetMethod(nameof(TypeManager.RegisterAdditionalPackableType), BindingFlags.Public | BindingFlags.Static);

	public static Type[] ValueTypes =
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

	public static bool IsTypeInitialized<T>() where T : class, IMemoryPackable, new()
	{
		return _registeredObjectTypes.Contains(typeof(T));
	}

	public static void RegisterAdditionalValueType<T>() where T : unmanaged
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

	public static void RegisterAdditionalPackableType<T>() where T : class, IMemoryPackable, new()
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

	//public static void RegisterAdditionalTypes(List<Type> additionalTypes)
	//{
	//	var cmdTypes = new List<Type>();
	//	var objTypes = new List<Type>();

	//	foreach (Type type in additionalTypes)
	//	{
	//		if (_registeredObjectTypes.Contains(type))
	//			throw new InvalidOperationException($"Type {type.Name} is already registered!");

	//		if (type.ContainsGenericParameters)
	//			throw new ArgumentException($"Type must be a concrete type!");

	//		if (!type.GetInterfaces().Any(i => i == typeof(IMemoryPackable)))
	//			throw new ArgumentException($"Type {type.Name} must have interface IMemoryPackable!");

	//		if (!type.IsClass)
	//			throw new ArgumentException($"Type {type.Name} must be a class!");
	//	}

	//	foreach (var type in additionalTypes)
	//	{
	//		if (!type.IsSubclassOf(typeof(PolymorphicMemoryPackableEntity<RendererCommand>)))
	//		{
	//			objTypes.Add(type);
	//		}
	//		else
	//		{
	//			cmdTypes.Add(type);
	//		}

	//		try
	//		{
	//			cmdTypes.Add(typeof(WrapperCommand<>).MakeGenericType(type));
	//		}
	//		catch
	//		{
	//			throw new ArgumentException($"Type {type.Name} is not compatible!");
	//		}
	//	}

	//	IdentifiableCommand.InitNewTypes(cmdTypes);

	//	foreach (var type in objTypes)
	//	{
	//		_registeredObjectTypes.Add(type);
	//	}
	//	foreach (var type in cmdTypes)
	//	{
	//		_registeredObjectTypes.Add(type);
	//	}
	//}
}