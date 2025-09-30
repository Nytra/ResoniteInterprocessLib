using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib;

public class MessagingHost
{
	private struct OwnerData
	{
		public readonly Dictionary<string, object?> ValueCallbacks = new();

		public readonly Dictionary<string, Action<string>?> StringCallbacks = new();

		public readonly Dictionary<string, Action?> Callbacks = new();

		public readonly Dictionary<string, object?> WrapperCallbacks = new();

		public OwnerData()
		{
		}
	}

	private MessagingManager _primary;

	private static MethodInfo? _handleValueCommandMethod = typeof(MessagingHost).GetMethod(nameof(HandleValueCommand), BindingFlags.Instance | BindingFlags.NonPublic);

	private static MethodInfo? _handleWrapperCommandMethod = typeof(MessagingHost).GetMethod(nameof(HandleWrapperCommand), BindingFlags.Instance | BindingFlags.NonPublic);

	public RenderCommandHandler? OnCommandReceived;

	public Action<Exception>? OnFailure;

	public Action<string>? OnWarning;

	public Action<string>? OnDebug;

	private Dictionary<string, OwnerData> _ownerData = new();

	public void RegisterOwner(string ownerName)
	{
		var ownerData = new OwnerData();
		_ownerData.Add(ownerName, ownerData);
	}

	public void RegisterValueCallback<T>(string owner, string id, Action<T> callback) where T : unmanaged
	{
		_ownerData[owner].ValueCallbacks[id] = callback;
	}

	public void RegisterStringCallback(string owner, string id, Action<string> callback)
	{
		_ownerData[owner].StringCallbacks[id] = callback;
	}

	public void RegisterCallback(string owner, string id, Action callback)
	{
		_ownerData[owner].Callbacks[id] = callback;
	}

	public void RegisterWrapperCallback<T>(string owner, string id, Action<T> callback) where T : class, IMemoryPackable, new()
	{
		_ownerData[owner].WrapperCallbacks[id] = callback;
	}

	public MessagingHost(bool isAuthority, string queueName, long queueCapacity, IMemoryPackerEntityPool pool)
	{
		_primary = new MessagingManager(pool);
		_primary.CommandHandler = CommandHandler;
		_primary.FailureHandler = FailHandler;
		_primary.WarningHandler = WarnHandler;
		_primary.Connect(queueName + "InterprocessLib", isAuthority, queueCapacity);

		if (!CommandTypeManager.InitializedTypes)
		{
			var list = new List<Type>();
			list.Add(typeof(IdentifiableCommand));
			list.Add(typeof(StringCommand));
			foreach (var valueType in Utils.ValueTypes)
				list.Add(typeof(ValueCommand<>).MakeGenericType(valueType));

			CommandTypeManager.RegisterAdditionalTypes(list);
			CommandTypeManager.InitializedTypes = true;
		}
	}

	private void HandleValueCommand<T>(ValueCommand<T> command) where T : unmanaged
	{
		OnDebug?.Invoke($"Received ValueCommand<{typeof(T).Name}>: {command.Owner}:{command.Id}:{command.Value}");
		if (_ownerData[command.Owner].ValueCallbacks.TryGetValue(command.Id, out object? callback))
		{
			if (callback != null)
			{
				((Action<T>)callback).Invoke(command.Value);
			}
		}
	}

	private void HandleStringCommand(StringCommand command)
	{
		OnDebug?.Invoke($"Received StringCommand: {command.Owner}:{command.Id}:{command.String ?? "NULL"}");
		if (_ownerData[command.Owner].StringCallbacks.TryGetValue(command.Id, out Action<string>? callback))
		{
			if (callback != null)
			{
				callback.Invoke(command.String);
			}
		}
	}

	private void HandleIdentifiableCommand(IdentifiableCommand command)
	{
		OnDebug?.Invoke($"Received IdentifiableCommand: {command.Owner}:{command.Id}");
		if (_ownerData[command.Owner].Callbacks.TryGetValue(command.Id, out Action? callback))
		{
			if (callback != null)
			{
				callback.Invoke();
			}
		}
	}

	private void HandleWrapperCommand<T>(WrapperCommand command) where T : class, IMemoryPackable, new()
	{
		OnDebug?.Invoke($"Received WrapperCommand<{command.ObjectType.Name}>: {command.Owner}:{command.Id}:{command.UntypedObject ?? "NULL"}");
		if (_ownerData[command.Owner].WrapperCallbacks.TryGetValue(command.Id, out object? callback))
		{
			if (callback != null)
			{
				((Action<T>)callback).Invoke((T)command.UntypedObject);
			}
		}
	}

	private void CommandHandler(RendererCommand command, int messageSize)
	{
		OnCommandReceived?.Invoke(command, messageSize);

		var commandType = command.GetType();
		if (commandType.IsGenericType)
		{
			var genDef = commandType.GetGenericTypeDefinition();
			if (genDef == typeof(ValueCommand<>))
			{
				var valueType = commandType.GetGenericArguments()[0];
				var typedMethod = _handleValueCommandMethod!.MakeGenericMethod(valueType);
				typedMethod.Invoke(this, new object[] { command });
			}
			else if (genDef == typeof(WrapperCommand<>))
			{
				var objectType = commandType.GetGenericArguments()[0];
				var typedMethod = _handleWrapperCommandMethod!.MakeGenericMethod(objectType);
				typedMethod.Invoke(this, new object[] { command });
			}
		}
		else
		{
			switch (command)
			{
				case StringCommand stringCommand:
					HandleStringCommand((StringCommand)command);
					break;
				case IdentifiableCommand identifiableCommand:
					HandleIdentifiableCommand((IdentifiableCommand)command);
					break;
				default:
					OnDebug?.Invoke($"Received RendererCommand: {command.GetType().Name}");
					break;
			}
		}
	}

	private void FailHandler(Exception ex)
	{
		OnFailure?.Invoke(ex);
	}

	private void WarnHandler(string msg)
	{
		OnWarning?.Invoke(msg);
	}

	public void SendCommand(RendererCommand command)
	{
		if (OnDebug is not null)
		{
			if (command is StringCommand stringCommand)
			{
				OnDebug.Invoke($"Sending StringCommand: {stringCommand.Owner}:{stringCommand.Id}:{stringCommand.String}");
			}
			else if (command is ValueCommand valueCommand)
			{
				OnDebug.Invoke($"Sending ValueCommand<{valueCommand.ValueType.Name}>: {valueCommand.Owner}:{valueCommand.Id}:{valueCommand.UntypedValue}");
			}
			else if (command is WrapperCommand wrapperCommand)
			{
				OnDebug.Invoke($"Sending WrapperCommand<{wrapperCommand.ObjectType.Name}>: {wrapperCommand.Owner}:{wrapperCommand.Id}:{wrapperCommand.UntypedObject ?? "NULL"}");
			}
			else if (command is IdentifiableCommand identifiableCommand)
			{
				OnDebug.Invoke($"Sending IdentifiableCommand: {identifiableCommand.Owner}:{identifiableCommand.Id}");
			}
			else
			{
				OnDebug.Invoke($"Sending RendererCommand: {command.GetType().Name}");
			}
		}
		_primary.SendCommand(command);
	}
}

internal static class CommandTypeManager
{
	private static readonly HashSet<Type> _registeredObjectTypes = new();

	internal static bool InitializedTypes = false;

	public static bool IsObjectTypeInitialized<T>() where T : class, IMemoryPackable, new()
	{
		return _registeredObjectTypes.Contains(typeof(T));
	}

	public static void RegisterAdditionalTypes(List<Type> additionalTypes)
	{
		var cmdTypes = new List<Type>();
		var objTypes = new List<Type>();
		foreach (Type type in additionalTypes)
		{
			if (_registeredObjectTypes.Contains(type))
				throw new InvalidOperationException($"Type {type.Name} is already registered!");

			if (type.ContainsGenericParameters)
				throw new ArgumentException($"Type must be a concrete type!");
		}

		foreach (var type in additionalTypes)
		{
			if (!type.IsSubclassOf(typeof(RendererCommand)))
			{
				objTypes.Add(type);
			}
			else
			{
				cmdTypes.Add(type);
			}
			try
			{
				cmdTypes.Add(typeof(WrapperCommand<>).MakeGenericType(type));
				
			}
			catch
			{
				throw new ArgumentException($"Type {type.Name} is not compatible!");
			}
		}

		IdentifiableCommand.InitNewTypes(cmdTypes);

		foreach (var type in objTypes)
		{
			_registeredObjectTypes.Add(type);
		}
		foreach (var type in cmdTypes)
		{
			_registeredObjectTypes.Add(type);
		}
	}
}

// IMPORTANT:
// RendererCommand derived classes MUST NOT have constructors because it breaks Unity for some reason

internal class IdentifiableCommand : RendererCommand
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

internal abstract class WrapperCommand : IdentifiableCommand
{
	public abstract object? UntypedObject { get; }
	public abstract Type ObjectType { get; }
}

internal class WrapperCommand<T> : WrapperCommand where T : class, IMemoryPackable, new()
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

internal class ValueCommand<T> : ValueCommand where T : unmanaged
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

internal class StringCommand : IdentifiableCommand
{
	public string String;

	public override void Pack(ref MemoryPacker packer)
	{
		base.Pack(ref packer);
		packer.Write(String);
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		base.Unpack(ref unpacker);
		unpacker.Read(ref String);
	}
}

internal static class Utils
{
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
}

public partial class Messenger
{
	private static MessagingHost? _host; 

	public static bool IsInitialized => _host is not null;

	internal static event RenderCommandHandler? OnCommandReceived;

	internal static Action<Exception>? OnFailure;

	internal static Action<string>? OnWarning;

	internal static Action<string>? OnDebug;

	private static List<Action>? PostInitActions = new();

	private string _ownerId;

	private static HashSet<string> _registeredOwnerIds = new();

	private List<Type>? _additionalCommandTypes;

	public Messenger(string ownerId, List<Type>? additionalCommandTypes = null)
	{
		if (ownerId is null)
			throw new ArgumentNullException(nameof(ownerId));

		if (_registeredOwnerIds.Contains(ownerId))
			throw new ArgumentException($"Owner \"{ownerId}\" is already registered!");

		_ownerId = ownerId;

		_registeredOwnerIds.Add(ownerId);

		_additionalCommandTypes = additionalCommandTypes;

		if (IsInitialized)
			RegisterWithBackend();
		else
			RunPostInit(RegisterWithBackend);
	}

	private void RegisterWithBackend()
	{
		_host!.RegisterOwner(_ownerId);
		if (_additionalCommandTypes is not null)
			CommandTypeManager.RegisterAdditionalTypes(_additionalCommandTypes);
	}

	private static void ThrowNotReady()
	{
		throw new InvalidOperationException("Messenger is not ready to be used yet!");
	}

	private static void FinishInitialization()
	{
		foreach (var action in PostInitActions!)
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				OnWarning?.Invoke($"Exception running post-init action:\n{ex}");
			}
		}
		PostInitActions = null;
	}

	private static void RunPostInit(Action act)
	{
		if (!IsInitialized)
			PostInitActions!.Add(act);
		else
			throw new InvalidOperationException("Already initialized!");
	}

	public void Send<T>(string id, T value) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => Send(id, value));
			return;
		}

		var command = new ValueCommand<T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Value = value;
		_host!.SendCommand(command);
	}

	public void Send(string id, string str)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));
		//if (str is null)
		//	throw new ArgumentNullException(nameof(str));

		if (!IsInitialized)
		{
			RunPostInit(() => Send(id, str));
			return;
		}

		var command = new StringCommand();
		command.Owner = _ownerId;
		command.Id = id;
		command.String = str;
		_host!.SendCommand(command);
	}

	public void Send(string id)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => Send(id));
			return;
		}

		var command = new IdentifiableCommand();
		command.Owner = _ownerId;
		command.Id = id;
		_host!.SendCommand(command);
	}

	public void Receive<T>(string id, Action<T> callback) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => Receive(id, callback));
			return;
		}

		_host!.RegisterValueCallback(_ownerId, id, callback);
	}

	public void Receive(string id, Action<string> callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => Receive(id, callback));
			return;
		}

		_host!.RegisterStringCallback(_ownerId, id, callback);
	}

	public void Receive(string id, Action callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => Receive(id, callback));
			return;
		}

		_host!.RegisterCallback(_ownerId, id, callback);
	}

	public void ReceiveObject<T>(string id, Action<T> callback) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => ReceiveObject(id, callback));
			return;
		}

		_host!.RegisterWrapperCallback(_ownerId, id, callback);
	}

	public void SendObject<T>(string id, T obj) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => SendObject(id, obj));
			return;
		}

		if (!CommandTypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {obj.GetType().Name} needs to be registered first!");

		var wrapper = new WrapperCommand<T>();
		wrapper.Object = obj;
		wrapper.Owner = _ownerId;
		wrapper.Id = id;

		_host!.SendCommand(wrapper);
	}
}