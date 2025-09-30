using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib;

internal class MessagingBackend
{
	private MessagingManager _primary;

	private static MethodInfo? _handleValueCommandMethod = typeof(MessagingBackend).GetMethod(nameof(HandleValueCommand), BindingFlags.Instance | BindingFlags.NonPublic);

	public RenderCommandHandler? OnCommandReceived;

	public Action<Exception>? OnFailure;

	public Action<string>? OnWarning;

	public Action<string>? OnDebug;

	private Dictionary<string, object?> _valueCallbackMap = new();

	private Dictionary<string, Action<string>?> _stringCallbackMap = new();

	private Dictionary<string, Action?> _callbackMap = new();

	public void RegisterValueCallback<T>(string id, Action<T> callback) where T : unmanaged
	{
		_valueCallbackMap[id] = callback;
	}

	public void RegisterStringCallback(string id, Action<string> callback)
	{
		_stringCallbackMap[id] = callback;
	}

	public void RegisterCallback(string id, Action callback)
	{
		_callbackMap[id] = callback;
	}

	public MessagingBackend(bool isAuthority, string queueName, long queueCapacity, IMemoryPackerEntityPool pool)
	{
		_primary = new MessagingManager(pool);
		_primary.CommandHandler = CommandHandler;
		_primary.FailureHandler = FailHandler;
		_primary.WarningHandler = WarnHandler;
		_primary.Connect(queueName + "InterprocessLib", isAuthority, queueCapacity);

		var newTypes = new List<Type>();
		newTypes.Add(typeof(IdentifiableCommand));
		newTypes.Add(typeof(StringCommand));
		foreach (var valueType in Utils._valueTypes)
			newTypes.Add(typeof(ValueCommand<>).MakeGenericType(valueType));
		IdentifiableCommand.InitNewTypes(newTypes);
	}

	private void HandleValueCommand<T>(ValueCommand<T> command) where T : unmanaged
	{
		OnDebug?.Invoke($"Received value command: {command.Id}:{command.Value}");
		if (_valueCallbackMap.TryGetValue(command.Id, out object? callback))
		{
			if (callback != null)
			{
				((Action<T>)callback).Invoke(command.Value);
			}
		}
	}

	private void HandleStringCommand(StringCommand command)
	{
		OnDebug?.Invoke($"Received string command: {command.Id}:{command.String}");
		if (_stringCallbackMap.TryGetValue(command.Id, out Action<string>? callback))
		{
			if (callback != null)
			{
				callback.Invoke(command.String);
			}
		}
	}

	private void HandleIdentifiableCommand(IdentifiableCommand command)
	{
		OnDebug?.Invoke($"Received identifiable command: {command.Id}");
		if (_callbackMap.TryGetValue(command.Id, out Action? callback))
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

		var cmdType = command.GetType();
		if (cmdType.IsGenericType)
		{
			if (cmdType.GetGenericTypeDefinition() == typeof(ValueCommand<>))
			{
				var valueType = cmdType.GetGenericArguments()[0];
				var typedMethod = _handleValueCommandMethod!.MakeGenericMethod(valueType);
				typedMethod.Invoke(this, new object[] { command });
			}
		}
		else
		{
			switch (command)
			{
				case StringCommand stringCommand:
					HandleStringCommand(stringCommand);
					break;
				case IdentifiableCommand identifiableCommand:
					HandleIdentifiableCommand(identifiableCommand);
					break;
				default:
					break;
			}
		}
	}

	void FailHandler(Exception ex)
	{
		OnFailure?.Invoke(ex);
	}

	void WarnHandler(string msg)
	{
		OnWarning?.Invoke(msg);
	}

	public void SendCommand(RendererCommand command)
	{
		_primary.SendCommand(command);
	}
}

// IMPORTANT:
// RendererCommand derived classes MUST NOT have constructors because it breaks Unity for some reason

internal class IdentifiableCommand : RendererCommand
{
	public string Id = "";

	public static void InitNewTypes(List<Type> extraTypes)
	{
		var list = new List<Type>();
		var theType = typeof(PolymorphicMemoryPackableEntity<RendererCommand>);
		var types = (List<Type>)theType.GetField("types", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
		list.AddRange(types);
		list.AddRange(extraTypes);
		InitTypes(list);
	}

	public override void Pack(ref MemoryPacker packer)
	{
		packer.Write(Id);
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		unpacker.Read(ref Id);
	}
}

internal class ValueCommand<T> : IdentifiableCommand where T : unmanaged
{
	public T Value;

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
	public static Type[] _valueTypes =
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

public static partial class Messaging
{
#pragma warning disable CS8618
	private static MessagingBackend _backend; // This will always be set by the static constructor
#pragma warning restore CS8618

	public static bool IsInitialized => _backend is not null;

	internal static event RenderCommandHandler? OnCommandReceived;

	internal static readonly Action<Exception>? OnFailure;

	internal static readonly Action<string>? OnWarning;

	internal static readonly Action<string>? OnDebug;

	internal static List<Action>? PostInitActions = new();

	private static void ThrowNotReady()
	{
		throw new InvalidOperationException("Messaging is not ready to be used yet!");
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

	public static void RunPostInit(Action act)
	{
		if (!IsInitialized)
			PostInitActions!.Add(act);
		else
		{
			act();
		}
	}

	public static void Send<T>(string id, T value) where T : unmanaged
	{
		var command = new ValueCommand<T>();
		command.Id = id;
		command.Value = value;
		_backend.SendCommand(command);
	}

	public static void Send(string id, string str)
	{
		var command = new StringCommand();
		command.Id = id;
		command.String = str;
		_backend.SendCommand(command);
	}

	public static void Send(string id)
	{
		var command = new IdentifiableCommand();
		command.Id = id;
		_backend.SendCommand(command);
	}

	public static void Receive<T>(string id, Action<T> callback) where T : unmanaged
	{
		_backend.RegisterValueCallback(id, callback);
	}

	public static void Receive(string id, Action<string> callback)
	{
		_backend.RegisterStringCallback(id, callback);
	}

	public static void Receive(string id, Action callback)
	{
		_backend.RegisterCallback(id, callback);
	}

	//public static void RegisterNewCommandType<T>() where T : RendererCommand
	//{
	//	IdentifiableCommand.InitNewTypes([typeof(T)]);
	//}

	//public static void Send(RendererCommand command)
	//{
	//	_backend.SendCommand(command);
	//}
}