using Renderite.Shared;
using System;
using System.Collections;
using System.Diagnostics.Eventing.Reader;
using System.Linq.Expressions;
using System.Reflection;

namespace InterprocessLib;

// IMPORTANT:
// RendererCommand derived classes MUST NOT have constructors because it breaks Unity for some reason

internal abstract class IdentifiableCommand : IMemoryPackable
{
	internal MessagingSystem? System;
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

internal sealed class EnumerableCommand : CollectionCommand
{
	public IEnumerable? Enumerable;

	public override IEnumerable? UntypedCollection => Enumerable;

	public override Type InnerDataType => Enumerable!.GetEnumerator().Current.GetType();

	public override Type CollectionType => Enumerable!.GetType();
}

internal abstract class ValueCollectionCommand : CollectionCommand
{
}

internal sealed class ValueCollectionCommand<C, T> : ValueCollectionCommand where C : ICollection<T>, new() where T : unmanaged
{
	public C? Values;

	public override IEnumerable? UntypedCollection => Values;
	public override Type InnerDataType => typeof(T);
	public override Type CollectionType => typeof(C);

	public override void Pack(ref MemoryPacker packer)
	{
		base.Pack(ref packer);
		packer.WriteValueList<C, T>(Values!);
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		base.Unpack(ref unpacker);
		unpacker.ReadValueList<C, T>(ref Values!);
	}
}

internal sealed class ValueArrayCommand<T> : ValueCollectionCommand where T : unmanaged
{
	public T[]? Values;

	public override IEnumerable? UntypedCollection => Values;
	public override Type InnerDataType => typeof(T);
	public override Type CollectionType => typeof(T[]);

	public override void Pack(ref MemoryPacker packer)
	{
		base.Pack(ref packer);
		if (Values is null)
		{
			packer.Write(-1);
			return;
		}
		int len = Values.Length;
		packer.Write(len);
		Span<T> data = packer.Access<T>(len);
		Values.CopyTo(data);
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
		for (int i = 0; i < len; i++)
		{
			T val = default;
			unpacker.Read(ref val);
			Values[i] = val;
		}
	}
}

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

internal abstract class ObjectCollectionCommand : CollectionCommand
{
}

internal sealed class ObjectListCommand<T> : ObjectCollectionCommand where T : class, IMemoryPackable, new()
{
	public List<T>? Objects;

	public override IEnumerable? UntypedCollection => Objects;
	public override Type InnerDataType => typeof(T);
	public override Type CollectionType => typeof(List<T>);

	public override void Pack(ref MemoryPacker packer)
	{
		base.Pack(ref packer);
		packer.WriteObjectList(Objects!);
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		base.Unpack(ref unpacker);
		unpacker.ReadObjectList(ref Objects!);
	}
}

internal sealed class ObjectArrayCommand<T> : ObjectCollectionCommand where T : class, IMemoryPackable, new()
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
			obj.Pack(ref packer);
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
			T obj = (T)(System?.TypeManager.Borrow(typeof(T)) ?? new T());
			obj.Unpack(ref unpacker);
			Objects[i] = obj;
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

		if (Packable is IdentifiableCommand identifiableCommand)
			identifiableCommand.System = backend;

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

		if (Packable is IdentifiableCommand identifiableCommand)
			identifiableCommand.System = backend;

		Packable!.Unpack(ref unpacker);
	}
}