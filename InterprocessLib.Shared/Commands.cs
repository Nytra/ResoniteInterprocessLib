using Renderite.Shared;
using System.Collections;

namespace InterprocessLib;

// IMPORTANT:
// RendererCommand derived classes MUST NOT have constructors because it breaks Unity for some reason

internal abstract class IdentifiableCommand : IMemoryPackable
{
	public string? Owner;
	public string? Id;

	public virtual void Pack(ref MemoryPacker packer)
	{
		packer.Write(Owner!);
		packer.Write(Id!);
	}

	public virtual void Unpack(ref MemoryUnpacker unpacker)
	{
		unpacker.Read(ref Owner!);
		unpacker.Read(ref Id!);
	}

	public override string ToString()
	{
		return $"IdentifiableCommand:{Owner}:{Id}";
	}
}

internal abstract class CollectionCommand : IdentifiableCommand
{
	public abstract IEnumerable? UntypedCollection { get; }
	public abstract Type StoredType { get; }
	public abstract Type CollectionType { get; }

	public override string ToString()
	{
		return $"CollectionCommand:{CollectionType.Name}<{StoredType.Name}>:{Owner}:{Id}:{UntypedCollection?.ToString() ?? "NULL"}";
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
	public override string ToString()
	{
		return $"EmptyCommand:{Owner}:{Id}";
	}
}

internal sealed class ValueCollectionCommand<C, T> : CollectionCommand where C : ICollection<T>?, new() where T : unmanaged
{
	public C? Values;

	public override IEnumerable? UntypedCollection => Values;

	public override Type StoredType => typeof(T);

	public override Type CollectionType => typeof(C);

	public override void Pack(ref MemoryPacker packer)
	{
		base.Pack(ref packer);
		var len = Values?.Count ?? -1;
		packer.Write(len);
		if (Values != null)
		{
			foreach (var value in Values)
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

	public override Type StoredType => typeof(T);

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
	}
}

internal sealed class TypeRegistrationCommand : TypeCommand
{
	public override string ToString()
	{
		return "TypeRegistrationCommand: " + Type?.FullName ?? "NULL";
	}
}

internal class TypeCommand : IdentifiableCommand
{
	public Type? Type;
	private static Dictionary<string, Type> _typeCache = new();

	public override void Pack(ref MemoryPacker packer)
	{
		base.Pack(ref packer);
		PackType(Type, ref packer);
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		base.Unpack(ref unpacker);
		Type = UnpackType(ref unpacker);
	}

	private void PackType(Type? type, ref MemoryPacker packer)
	{
		if (type is null)
		{
			packer.Write(false);
		}
		else
		{
			packer.Write(true);
			if (type.IsGenericType)
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
				packer.Write(type.FullName!);
			}
		}
	}

	private Type? UnpackType(ref MemoryUnpacker unpacker)
	{
		var hasType = unpacker.Read<bool>();
		if (!hasType) return null;

		var isGenericType = unpacker.Read<bool>();
		if (isGenericType)
		{
			var genericTypeDefinitionName = unpacker.ReadString();
			int numTypeArgs = unpacker.Read<int>();
			var typeArgs = new Type?[numTypeArgs];
			for (int i = 0; i < numTypeArgs; i++)
			{
				typeArgs[i] = UnpackType(ref unpacker);
			}

			if (typeArgs.Any(t => t is null)) return null;

			var genericTypeDefinition = FindType(genericTypeDefinitionName);
			if (genericTypeDefinition != null)
			{
				return genericTypeDefinition.MakeGenericType(typeArgs!);
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
		//Messenger.OnDebug?.Invoke($"Looking for Type: {typeString}");
		if (_typeCache.TryGetValue(typeString, out var type))
		{
			Messenger.OnDebug?.Invoke($"Found Type in cache: {type.FullName}");
			return type;
		}
		
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
			Messenger.OnDebug?.Invoke($"Found Type to add to cache: {type.FullName}");
			_typeCache[typeString] = type;
		}
		else
		{
			Messenger.OnWarning?.Invoke($"Could not find the Type: {typeString}");
		}
		return type;
	}

	public override string ToString()
	{
		return $"TypeCommand: {Type?.FullName ?? "NULL"}:{Owner}:{Id}";
	}
}

internal sealed class StringArrayCommand : CollectionCommand
{
	public string?[]? Strings;

	public override IEnumerable? UntypedCollection => Strings;
	public override Type StoredType => typeof(string);
	public override Type CollectionType => typeof(string[]);

	public override void Pack(ref MemoryPacker packer)
	{
		base.Pack(ref packer);
		if (Strings is null)
		{
			packer.Write(-1);
			return;
		}
		int len = Strings.Length;
		packer.Write(len);
		foreach (var str in Strings)
		{
			packer.Write(str!);
		}
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		base.Unpack(ref unpacker);
		int len = 0;
		unpacker.Read(ref len);
		if (len == -1)
		{
			Strings = null;
			return;
		}
		Strings = new string[len]; // ToDo: use pool borrowing here?
		for (int i = 0; i < len; i++)
		{
			unpacker.Read(ref Strings[i]!);
		}
	}
}

internal sealed class StringCollectionCommand<C> : CollectionCommand where C : ICollection<string>?, new()
{
	public IReadOnlyCollection<string?>? Strings; // IReadOnlyCollection is required for covariance of string? and string

	public override IEnumerable? UntypedCollection => Strings;
	public override Type StoredType => typeof(string);
	public override Type CollectionType => typeof(C);

	public override void Pack(ref MemoryPacker packer)
	{
		base.Pack(ref packer);
		if (Strings is null)
		{
			packer.Write(-1);
			return;
		}
		int len = Strings.Count;
		packer.Write(len);
		foreach (var str in Strings)
		{
			packer.Write(str!);
		}
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		base.Unpack(ref unpacker);
		int len = 0;
		unpacker.Read(ref len);
		if (len == -1)
		{
			Strings = default;
			return;
		}
		var collection = new C(); // ToDo: use pool borrowing here?
		for (int i = 0; i < len; i++)
		{
			string? str = default;
			unpacker.Read(ref str!);
			collection.Add(str);
		}
		Strings = (IReadOnlyCollection<string?>?)collection;
	}
}

internal sealed class ObjectCollectionCommand<C, T> : CollectionCommand where C : ICollection<T>?, new() where T : class?, IMemoryPackable?, new()
{
	public C? Objects;

	public override IEnumerable? UntypedCollection => Objects;
	public override Type StoredType => typeof(T);
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
#pragma warning disable CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
#pragma warning disable CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
            packer.WriteObject(obj);
#pragma warning restore CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
#pragma warning restore CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
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
			T obj = new();
#pragma warning disable CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
#pragma warning disable CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
            unpacker.ReadObject(ref obj!);
#pragma warning restore CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
#pragma warning restore CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
            Objects.Add(obj);
		}
	}
}

internal sealed class ObjectArrayCommand<T> : CollectionCommand where T : class?, IMemoryPackable?, new()
{
	public T?[]? Objects;

	public override IEnumerable? UntypedCollection => Objects;
	public override Type StoredType => typeof(T);
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
#pragma warning disable CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
#pragma warning disable CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
            packer.WriteObject(obj);
#pragma warning restore CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
#pragma warning restore CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
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
#pragma warning disable CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
#pragma warning disable CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
            unpacker.ReadObject(ref Objects[i]!);
#pragma warning restore CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
#pragma warning restore CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
        }
	}
}

internal sealed class ObjectCommand<T> : ObjectCommand where T : class?, IMemoryPackable?, new()
{
	public T? Object;

	public override object? UntypedObject => Object;
	public override Type ObjectType => typeof(T);

	public override void Pack(ref MemoryPacker packer)
	{
		base.Pack(ref packer);
#pragma warning disable CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
#pragma warning disable CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
        packer.WriteObject(Object);
#pragma warning restore CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
#pragma warning restore CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
    }

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		base.Unpack(ref unpacker);
#pragma warning disable CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
#pragma warning disable CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
        unpacker.ReadObject(ref Object);
#pragma warning restore CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
#pragma warning restore CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
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

internal sealed class MessengerReadyCommand : IMemoryPackable
{
	public void Pack(ref MemoryPacker packer)
	{
	}

	public void Unpack(ref MemoryUnpacker unpacker)
	{
	}
}

internal sealed class PingCommand : IMemoryPackable
{
	public DateTime SentTime;
	public DateTime? ReceivedTime;
	public void Pack(ref MemoryPacker packer)
	{
		packer.Write(SentTime);
		packer.Write(ReceivedTime);
	}

	public void Unpack(ref MemoryUnpacker unpacker)
	{
		unpacker.Read(ref SentTime);
		unpacker.Read(ref ReceivedTime);
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
		if (QueueName is null) throw new ArgumentNullException(nameof(QueueName));

		var system = MessagingSystem.TryGetRegisteredSystem(QueueName!);

		if (system is null) throw new InvalidOperationException($"MessagingSystem with QueueName: {QueueName} is not registered.");

		if (Packable is null)
		{
			packer.Write(-1);
			return;
		}

		var packedType = Packable.GetType();
		packer.Write(system.OutgoingTypeManager.GetTypeIndex(packedType));
		packer.Write(QueueName!);
		Packable.Pack(ref packer);
		system.OutgoingTypeManager.Return(packedType, Packable);
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

		if (backend is null) throw new InvalidDataException($"MessagingSystem with QueueName: {QueueName} is not registered.");

		var type = backend!.IncomingTypeManager.GetTypeFromIndex(TypeIndex);

		Packable = backend.IncomingTypeManager.Borrow(type);

		Packable.Unpack(ref unpacker);
	}
}