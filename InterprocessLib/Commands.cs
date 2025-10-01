using Renderite.Shared;
using System.Collections;
using System.Reflection;

namespace InterprocessLib;

// IMPORTANT:
// RendererCommand derived classes MUST NOT have constructors because it breaks Unity for some reason

internal abstract class IdentifiableCommand : RendererCommand
{
	internal string Owner = "";
	public string Id = "";

	public static void InitNewTypes(List<Type> newTypes)
	{
		var list = new List<Type>();
		
		//list.AddRange(types);
		list.AddRange(newTypes);
		InitTypes(list);
	}

	public override void Pack(ref MemoryPacker packer)
	{
		packer.Write(Owner);
		packer.Write(Id);
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
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
#pragma warning disable CS8604
		packer.WriteValueList<ICollection<T>, T>(Values);
#pragma warning restore
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		base.Unpack(ref unpacker);
#pragma warning disable CS8601
		unpacker.ReadValueList<C, T>(ref Values);
#pragma warning restore
	}
}

//internal sealed class ValueHashSetCommand<T> : CollectionCommand where T : unmanaged
//{
//	public HashSet<T>? Values;

//	public override ICollection? UntypedCollection => (ICollection?)Values;
//	public override Type InnerDataType => typeof(T);

//	public override void Pack(ref MemoryPacker packer)
//	{
//		base.Pack(ref packer);
//#pragma warning disable CS8604
//		packer.WriteValueList<HashSet<T>, T>(Values);
//#pragma warning restore
//	}

//	public override void Unpack(ref MemoryUnpacker unpacker)
//	{
//		base.Unpack(ref unpacker);
//#pragma warning disable CS8601
//		unpacker.ReadValueList<HashSet<T>, T>(ref Values);
//#pragma warning restore
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
#pragma warning disable CS8604
		packer.WriteStringList(Values);
#pragma warning restore
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		base.Unpack(ref unpacker);
#pragma warning disable CS8601
		unpacker.ReadStringList(ref Values);
#pragma warning restore
	}
}

internal sealed class ObjectListCommand<T> : CollectionCommand where T : class, IMemoryPackable, new()
{
	public List<T>? Values;

	public override IEnumerable? UntypedCollection => Values;
	public override Type InnerDataType => typeof(T);
	public override Type CollectionType => typeof(List<T>);

	public override void Pack(ref MemoryPacker packer)
	{
		base.Pack(ref packer);
#pragma warning disable CS8604
		packer.WriteObjectList(Values);
#pragma warning restore
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		base.Unpack(ref unpacker);
#pragma warning disable CS8601
		unpacker.ReadObjectList(ref Values);
#pragma warning restore
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
#pragma warning disable CS8604
		packer.Write(String);
#pragma warning restore
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		base.Unpack(ref unpacker);
#pragma warning disable CS8601
		unpacker.Read(ref String);
#pragma warning restore
	}

	public override string ToString()
	{
		return $"StringCommand:{Owner}:{Id}:{String ?? "NULL"}";
	}
}

internal sealed class MessengerReadyCommand : RendererCommand
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