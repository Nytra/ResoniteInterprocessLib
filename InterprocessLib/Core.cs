using Renderite.Shared;
using System.Reflection;
using System.Runtime.CompilerServices;

//[assembly: InternalsVisibleTo("InterprocessLib.BepInEx")]
//[assembly: InternalsVisibleTo("InterprocessLib.BepisLoader")]

namespace InterprocessLib;

internal class MessagingHost
{
	private MessagingManager _primary;

	public string QueueName { get; private set; }

	public long QueueCapacity { get; private set; }

	private static MethodInfo? _handleValueCommandMethod = typeof(MessagingHost).GetMethod(nameof(HandleValueCommand), BindingFlags.Instance | BindingFlags.NonPublic);

	public event RenderCommandHandler? OnCommandReceieved;

	public static MessagingHost? Instance { get; private set; }

	public event Action<Exception>? OnFailure;

	public event Action<string>? OnWarning;

	public event Action<string>? OnDebug;

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

	public MessagingHost(bool isAuthority, string queueName, long queueCapacity, IMemoryPackerEntityPool pool)
	{
		Instance = this;

		QueueName = queueName;
		QueueCapacity = queueCapacity;

		_primary = new MessagingManager(pool);
		_primary.CommandHandler = HandleCommand;
		_primary.FailureHandler = FailHandler;
		_primary.WarningHandler = WarnHandler;
		_primary.Connect(QueueName + "InterprocessLib", isAuthority, QueueCapacity);

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

	private void HandleCommand(RendererCommand command, int messageSize)
	{
		OnCommandReceieved?.Invoke(command, messageSize);

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

	internal void Debug(string msg)
	{
		OnDebug?.Invoke(msg);
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
	internal static MessagingHost Host;

	private static void ThrowNotReady()
	{
		throw new InvalidOperationException("Messaging is not ready to be used yet!");
	}

	public static void Send<T>(string id, T value) where T : unmanaged
	{
		var command = new ValueCommand<T>();
		command.Id = id;
		command.Value = value;
		Host.SendCommand(command);
	}

	public static void Send(string id, string str)
	{
		var command = new StringCommand();
		command.Id = id;
		command.String = str;
		Host.SendCommand(command);
	}

	public static void Send(string id)
	{
		var command = new IdentifiableCommand();
		command.Id = id;
		Host.SendCommand(command);
	}

	public static void Receive<T>(string id, Action<T> callback) where T : unmanaged
	{
		Host.RegisterValueCallback(id, callback);
	}

	public static void Receive(string id, Action<string> callback)
	{
		Host.RegisterStringCallback(id, callback);
	}

	public static void Receive(string id, Action callback)
	{
		Host.RegisterCallback(id, callback);
	}
}