using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib;

// IMPORTANT:
// RendererCommand derived classes MUST NOT have constructors because it breaks Unity for some reason

internal sealed class MessengerReadyCommand : RendererCommand
{
	public override void Pack(ref MemoryPacker packer)
	{
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
	}
}

internal abstract class IdentifiableCommand : RendererCommand
{
	internal string Owner = "";
	public string Id = "";

	public static void InitNewTypes(List<Type> newTypes)
	{
		var list = new List<Type>();
		var theType = typeof(PolymorphicMemoryPackableEntity<RendererCommand>);
		var types = (List<Type>)theType.GetField("types", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
		list.AddRange(types);
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
}

internal sealed class EmptyCommand : IdentifiableCommand
{
	// owo
}

internal abstract class WrapperCommand : IdentifiableCommand
{
	public abstract object? UntypedObject { get; }
	public abstract Type ObjectType { get; }
}

internal sealed class WrapperCommand<T> : WrapperCommand where T : class, IMemoryPackable, new()
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

internal abstract class ValueCommand : IdentifiableCommand
{
	public abstract object UntypedValue { get; }
	public abstract Type ValueType { get; }
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
}