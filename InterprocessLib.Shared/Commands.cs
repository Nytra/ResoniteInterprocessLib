using Renderite.Shared;
using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics.Eventing.Reader;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace InterprocessLib;

// IMPORTANT:
// RendererCommand derived classes MUST NOT have constructors because it breaks Unity for some reason

internal abstract class IdentifiableCommand : IMemoryPackable
{
	internal string Owner = "";
	public string Id = "";

	public virtual void Pack(ref MemoryPacker packer)
	{
		packer.Write(Owner);
		packer.Write(Id);
	}

	public virtual void Unpack(ref MemoryUnpacker unpacker)
	{
		unpacker.Read(ref Owner);
		unpacker.Read(ref Id);
	}

	public override string ToString()
	{
		return $"IdentifiableCommand:{Owner}:{Id}";
	}
}

internal abstract class CollectionCommand : IdentifiableCommand
{
	public abstract IEnumerable? UntypedCollection { get; }
	public abstract Type InnerDataType { get; }
	public abstract Type CollectionType { get; }

	public override string ToString()
	{
		return $"CollectionCommand:{CollectionType.Name}<{InnerDataType.Name}>:{Owner}:{Id}:{UntypedCollection?.ToString() ?? "NULL"}";
	}
}

internal abstract class ValueCommand : IdentifiableCommand
{
	public abstract object UntypedValue { get; }
	public abstract Type ValueType { get; }

	public override string ToString()
	{
		return $"ValueCommand<{ValueType.Name}>:{Owner}:{Id}:{UntypedValue}";
	}
}

internal abstract class ObjectCommand : IdentifiableCommand
{
	public abstract object? UntypedObject { get; }
	public abstract Type ObjectType { get; }

	public override string ToString()
	{
		return $"ObjectCommand<{ObjectType.Name}>:{Owner}:{Id}:{UntypedObject?.ToString() ?? "NULL"}";
	}
}

internal sealed class EmptyCommand : IdentifiableCommand
{
	// owo

	public override string ToString()
	{
		return $"EmptyCommand:{Owner}:{Id}";
	}
}

internal sealed class ValueCollectionCommand<C, T> : CollectionCommand where C : ICollection<T>, new() where T : unmanaged
{
	public C? Values;

	public override IEnumerable? UntypedCollection => Values;

	public override Type InnerDataType => typeof(T);

	public override Type CollectionType => typeof(C);

	public override void Pack(ref MemoryPacker packer)
	{
		base.Pack(ref packer);
		var len = Values?.Count ?? -1;
		packer.Write(len);
		if (Values != null)
		{
			foreach (var value in Values!)
			{
				packer.Write(value);
			}
		}
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		base.Unpack(ref unpacker);
		int len = 0;
		unpacker.Read(ref len);
		if (len == -1)
		{
			Values = default;
			return;
		}
		Values = new C(); // ToDo: use pool borrowing here?
		for (int i = 0; i < len; i++)
		{
			T val = default;
			unpacker.Read(ref val);
			Values.Add(val);
		}
	}
}

internal sealed class ValueArrayCommand<T> : CollectionCommand where T : unmanaged
{
	public T[]? Values;

	public override IEnumerable? UntypedCollection => Values;

	public override Type InnerDataType => typeof(T);

	public override Type CollectionType => typeof(T[]);

	public override void Pack(ref MemoryPacker packer)
	{
		base.Pack(ref packer);
		var len = Values?.Length ?? -1;
		packer.Write(len);
		if (Values != null)
		{
			Span<T> data = packer.Access<T>(len);
			Values.CopyTo(data);
		}
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		base.Unpack(ref unpacker);
		int len = 0;
		unpacker.Read(ref len);
		if (len == -1)
		{
			Values = null;
			return;
		}
		Values = new T[len]; // ToDo: use pool borrowing here?
		ReadOnlySpan<T> data = unpacker.Access<T>(len);
		data.CopyTo(Values);

		//for (int i = 0; i < len; i++)
		//{
		//	T val = default;
		//	unpacker.Read(ref val);
		//	Values[i] = val;
		//}
	}
}

//internal sealed class ValueDictionaryCommand<TKey, TValue> : CollectionCommand where TKey : unmanaged where TValue : unmanaged
//{
//	public Dictionary<TKey, TValue>? Dict;

//	public override IEnumerable? UntypedCollection => Dict;

//	public override Type InnerDataType => typeof(KeyValuePair<TKey, TValue>);

//	public override Type CollectionType => typeof(Dictionary<TKey, TValue>);

//	public override void Pack(ref MemoryPacker packer)
//	{
//		base.Pack(ref packer);
//		var len = Dict?.Count ?? -1;
//		packer.Write(len);
//		if (Dict != null)
//		{
//			foreach (var kvp in Dict)
//			{
//				packer.Write(kvp.Key);
//				packer.Write(kvp.Value);
//			}
//		}
//	}

//	public override void Unpack(ref MemoryUnpacker unpacker)
//	{
//		base.Unpack(ref unpacker);
//		int len = 0;
//		unpacker.Read(ref len);
//		if (len == -1)
//		{
//			Dict = null;
//			return;
//		}
//		Dict = new(); // ToDo: use pool borrowing here?
//		for (int i = 0; i < len; i++)
//		{
//			TKey key = default;
//			unpacker.Read(ref key);
//			TValue val = default;
//			unpacker.Read(ref val);
//			Dict[key] = val;
//		}
//	}
//}

internal sealed class TypeCommand : IMemoryPackable
{
	public Type? Type;
	private static Dictionary<string, Type> _typeCache = new();

	public void Pack(ref MemoryPacker packer)
	{
		PackType(Type!, ref packer);
	}

	public void Unpack(ref MemoryUnpacker unpacker)
	{
		Type = UnpackType(ref unpacker);
	}

	private void PackType(Type type, ref MemoryPacker packer)
	{
		if (type!.IsGenericType)
		{
			packer.Write(true);
			var genericTypeDefinition = type.GetGenericTypeDefinition();
			packer.Write(genericTypeDefinition.FullName!);
			var typeArgs = type.GetGenericArguments();
			packer.Write(typeArgs.Length);
			foreach (var typeArg in typeArgs)
			{
				PackType(typeArg, ref packer);
			}
		}
		else
		{
			packer.Write(false);
			packer.Write(type!.FullName!);
		}
	}

	private Type? UnpackType(ref MemoryUnpacker unpacker)
	{
		var isGenericType = unpacker.Read<bool>();
		if (isGenericType)
		{
			var genericTypeDefinitionName = unpacker.ReadString();
			int numTypeArgs = unpacker.Read<int>();
			var typeArgs = new Type[numTypeArgs];
			for (int i = 0; i < numTypeArgs; i++)
			{
				typeArgs[i] = UnpackType(ref unpacker)!;
			}

			if (typeArgs.Any(t => t is null)) return null;

			var genericTypeDefinition = FindType(genericTypeDefinitionName);
			if (genericTypeDefinition != null)
			{
				return genericTypeDefinition.MakeGenericType(typeArgs);
			}
			else
			{
				return null;
			}
		}
		else
		{
			var typeString = unpacker.ReadString();
			return FindType(typeString);
		}
	}

	private Type? FindType(string typeString)
	{
		if (_typeCache.TryGetValue(typeString, out var type))
		{
			return type;
		}

		Messenger.OnDebug?.Invoke($"Looking for Type: {typeString}");
		type = Type.GetType(typeString);
		if (type is null)
		{
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				type = asm.GetType(typeString);
				if (type != null) break;
			}
		}
		if (type != null)
		{
			Messenger.OnDebug?.Invoke($"Found Type: {type.FullName}");
			_typeCache[typeString] = type;
		}
		else
		{
			Messenger.OnDebug?.Invoke($"Could not find type.");
		}
		return type;
	}

	public override string ToString()
	{
		return "TypeCommand: " + Type?.FullName ?? "NULL";
	}
}

//internal sealed class ObjectDictionaryCommand<TKey, TValue> : CollectionCommand where TKey : class, IMemoryPackable, new() where TValue : class, IMemoryPackable, new()
//{
//	public Dictionary<TKey, TValue>? Dict;

//	public override IEnumerable? UntypedCollection => Dict;

//	public override Type InnerDataType => typeof(KeyValuePair<TKey, TValue>);

//	public override Type CollectionType => typeof(Dictionary<TKey, TValue>);

//	public override void Pack(ref MemoryPacker packer)
//	{
//		base.Pack(ref packer);
//		var len = Dict?.Count ?? -1;
//		packer.Write(len);
//		if (Dict != null)
//		{
//			foreach (var kvp in Dict)
//			{
//				packer.WriteObject(kvp.Key);
//				packer.WriteObject(kvp.Value);
//			}
//		}
//	}

//	public override void Unpack(ref MemoryUnpacker unpacker)
//	{
//		base.Unpack(ref unpacker);
//		int len = 0;
//		unpacker.Read(ref len);
//		if (len == -1)
//		{
//			Dict = null;
//			return;
//		}
//		Dict = new(); // ToDo: use pool borrowing here?
//		for (int i = 0; i < len; i++)
//		{
//			TKey key = default!;
//			unpacker.ReadObject(ref key!);
//			TValue val = default!;
//			unpacker.ReadObject(ref val!);
//			Dict[key] = val;
//		}
//	}
//}

internal sealed class StringListCommand : CollectionCommand
{
	public List<string>? Values;

	public override IEnumerable? UntypedCollection => Values;
	public override Type InnerDataType => typeof(string);
	public override Type CollectionType => typeof(List<string>);

	public override void Pack(ref MemoryPacker packer)
	{
		base.Pack(ref packer);
		packer.WriteStringList(Values!);
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		base.Unpack(ref unpacker);
		unpacker.ReadStringList(ref Values!);
	}
}

internal sealed class ObjectCollectionCommand<C, T> : CollectionCommand where C : ICollection<T>, new() where T : class, IMemoryPackable, new()
{
	public C? Objects;

	public override IEnumerable? UntypedCollection => Objects;
	public override Type InnerDataType => typeof(T);
	public override Type CollectionType => typeof(C);

	public override void Pack(ref MemoryPacker packer)
	{
		base.Pack(ref packer);
		if (Objects is null)
		{
			packer.Write(-1);
			return;
		}
		int len = Objects.Count;
		packer.Write(len);
		foreach (var obj in Objects)
		{
			packer.WriteObject(obj);
		}
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		base.Unpack(ref unpacker);
		int len = 0;
		unpacker.Read(ref len);
		if (len == -1)
		{
			Objects = default;
			return;
		}
		Objects = new C(); // ToDo: use pool borrowing here?
		for (int i = 0; i < len; i++)
		{
			T obj = default!;
			unpacker.ReadObject(ref obj!);
			Objects.Add(obj);
		}
	}
}

internal sealed class ObjectArrayCommand<T> : CollectionCommand where T : class, IMemoryPackable, new()
{
	public T[]? Objects;

	public override IEnumerable? UntypedCollection => Objects;
	public override Type InnerDataType => typeof(T);
	public override Type CollectionType => typeof(T[]);

	public override void Pack(ref MemoryPacker packer)
	{
		base.Pack(ref packer);
		if (Objects is null)
		{
			packer.Write(-1);
			return;
		}
		int len = Objects.Length;
		packer.Write(len);
		foreach (var obj in Objects)
		{
			packer.WriteObject(obj);
		}
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		base.Unpack(ref unpacker);
		int len = 0;
		unpacker.Read(ref len);
		if (len == -1)
		{
			Objects = null;
			return;
		}
		Objects = new T[len]; // ToDo: use pool borrowing here?
		for (int i = 0; i < len; i++)
		{
			unpacker.ReadObject(ref Objects[i]!);
		}
	}
}

internal sealed class ObjectCommand<T> : ObjectCommand where T : class, IMemoryPackable, new()
{
	public T? Object;

	public override object? UntypedObject => Object;
	public override Type ObjectType => typeof(T);

	public override void Pack(ref MemoryPacker packer)
	{
		base.Pack(ref packer);
		packer.WriteObject(Object);
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		base.Unpack(ref unpacker);
		unpacker.ReadObject(ref Object);
	}
}

internal sealed class ValueCommand<T> : ValueCommand where T : unmanaged
{
	public T Value;

	public override object UntypedValue => Value;
	public override Type ValueType => typeof(T);

	public override void Pack(ref MemoryPacker packer)
	{
		base.Pack(ref packer);
		packer.Write(Value);
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		base.Unpack(ref unpacker);
		unpacker.Read(ref Value);
	}
}

internal sealed class StringCommand : IdentifiableCommand
{
	public string? String;

	public override void Pack(ref MemoryPacker packer)
	{
		base.Pack(ref packer);
		packer.Write(String!);
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		base.Unpack(ref unpacker);
		unpacker.Read(ref String!);
	}

	public override string ToString()
	{
		return $"StringCommand:{Owner}:{Id}:{String ?? "NULL"}";
	}
}

internal sealed class MessengerReadyCommand : IdentifiableCommand
{
	public override void Pack(ref MemoryPacker packer)
	{
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
	}

	public override string ToString()
	{
		return $"MessengerReadyCommand";
	}
}

internal sealed class WrapperCommand : RendererCommand
{
	public int TypeIndex;
	public string? QueueName;
	public IMemoryPackable? Packable;

	public static void InitNewTypes(List<Type> types)
	{
		InitTypes(types);
	}

	public override void Pack(ref MemoryPacker packer)
	{
		var packedType = Packable?.GetType();
		var backend = MessagingSystem.TryGetRegisteredSystem(QueueName!);
		var type = Packable is null ? -1 : backend!.TypeManager.GetTypeIndex(packedType!);
		packer.Write(type);

		if (type == -1)
			return;

		packer.Write(QueueName!);

		Packable!.Pack(ref packer);

		backend!.TypeManager.Return(packedType!, Packable);
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		unpacker.Read(ref TypeIndex);

		if (TypeIndex == -1)
		{
			Packable = null;
			return;
		}

		unpacker.Read(ref QueueName!);

		var backend = MessagingSystem.TryGetRegisteredSystem(QueueName);
		var type = backend!.TypeManager.GetTypeFromIndex(TypeIndex);

		Packable = backend.TypeManager.Borrow(type);

		Packable!.Unpack(ref unpacker);
	}
}