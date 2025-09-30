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

		public OwnerData()
		{
		}
	}

	internal static List<Type> CommandTypes = new(); // It's a list to guarantee order

	private MessagingManager _primary;

	private static MethodInfo? _handleValueCommandMethod = typeof(MessagingHost).GetMethod(nameof(HandleValueCommand), BindingFlags.Instance | BindingFlags.NonPublic);

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

	public MessagingHost(bool isAuthority, string queueName, long queueCapacity, IMemoryPackerEntityPool pool)
	{
		_primary = new MessagingManager(pool);
		_primary.CommandHandler = CommandHandler;
		_primary.FailureHandler = FailHandler;
		_primary.WarningHandler = WarnHandler;
		_primary.Connect(queueName + "InterprocessLib", isAuthority, queueCapacity);

		CommandTypes.Add(typeof(IdentifiableCommand));
		CommandTypes.Add(typeof(StringCommand));
		foreach (var valueType in Utils.ValueTypes)
			CommandTypes.Add(typeof(ValueCommand<>).MakeGenericType(valueType));

		IdentifiableCommand.InitNewTypes();
	}

	private void HandleValueCommand<T>(ValueCommand<T> command) where T : unmanaged
	{
		OnDebug?.Invoke($"Received value command: {command.Owner}:{command.Id}:{command.Value}");
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
		OnDebug?.Invoke($"Received string command: {command.Owner}:{command.Id}:{command.String}");
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
		OnDebug?.Invoke($"Received identifiable command: {command.Owner}:{command.Id}");
		if (_ownerData[command.Owner].Callbacks.TryGetValue(command.Id, out Action? callback))
		{
			if (callback != null)
			{
				callback.Invoke();
			}
		}
	}

	private void CommandHandler(RendererCommand command, int messageSize)
	{
		OnCommandReceived?.Invoke(command, messageSize);

		var commandType = command.GetType();
		if (commandType.IsGenericType)
		{
			if (commandType.GetGenericTypeDefinition() == typeof(ValueCommand<>))
			{
				var valueType = commandType.GetGenericArguments()[0];
				var typedMethod = _handleValueCommandMethod!.MakeGenericMethod(valueType);
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
				OnDebug.Invoke($"Sending ValueCommand: {valueCommand.Owner}:{valueCommand.Id}:{valueCommand.UntypedValue}");
			}
			else if (command is IdentifiableCommand identifiableCommand)
			{
				OnDebug.Invoke($"Sending IdentifiableCommand: {identifiableCommand.Owner}:{identifiableCommand.Id}");
			}
		}
		_primary.SendCommand(command);
	}
}

// IMPORTANT:
// RendererCommand derived classes MUST NOT have constructors because it breaks Unity for some reason

internal class IdentifiableCommand : RendererCommand
{
	internal string Owner = "";
	public string Id = "";

	public static void InitNewTypes()
	{
		var list = new List<Type>();
		var theType = typeof(PolymorphicMemoryPackableEntity<RendererCommand>);
		var types = (List<Type>)theType.GetField("types", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
		list.AddRange(types);
		list.AddRange(MessagingHost.CommandTypes);
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

internal abstract class ValueCommand : IdentifiableCommand
{
	public abstract object UntypedValue { get; }
}

internal class ValueCommand<T> : ValueCommand where T : unmanaged
{
	public T Value;
	public override object UntypedValue => Value;

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
	public string String = "";

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
#pragma warning disable CS8618
	private static MessagingHost _host; // This will always be set by the static constructor
#pragma warning restore CS8618

	public static bool IsInitialized => _host is not null;

	internal static event RenderCommandHandler? OnCommandReceived;

	internal static Action<Exception>? OnFailure;

	internal static Action<string>? OnWarning;

	internal static Action<string>? OnDebug;

	internal static List<Action>? PostInitActions = new();

	private string _ownerId;

	private static HashSet<string> _registeredOwnerIds = new();

	public Messenger(string ownerId)
	{
		if (ownerId is null)
			throw new ArgumentNullException(nameof(ownerId));

		if (_registeredOwnerIds.Contains(ownerId))
			throw new ArgumentException($"Owner \"{ownerId}\" is already registered!");

		_ownerId = ownerId;

		_registeredOwnerIds.Add(ownerId);

		if (IsInitialized)
			RegisterWithBackend();
		else
			RunPostInit(RegisterWithBackend);
	}

	private void RegisterWithBackend()
	{
		_host.RegisterOwner(_ownerId);
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
		_host.SendCommand(command);
	}

	public void Send(string id, string str)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));
		if (str is null)
			throw new ArgumentNullException(nameof(str));

		if (!IsInitialized)
		{
			RunPostInit(() => Send(id, str));
			return;
		}

		var command = new StringCommand();
		command.Owner = _ownerId;
		command.Id = id;
		command.String = str;
		_host.SendCommand(command);
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
		_host.SendCommand(command);
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

		_host.RegisterValueCallback(_ownerId, id, callback);
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

		_host.RegisterStringCallback(_ownerId, id, callback);
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

		_host.RegisterCallback(_ownerId, id, callback);
	}
}