using HarmonyLib;
using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib.Shared;

public class MessagingHost
{
	private MessagingManager _primary;

	public string QueueName { get; private set; }

	public long QueueCapacity { get; private set; }

	private static MethodInfo _handleValueCommandMethod = AccessTools.Method(typeof(MessagingHost), nameof(HandleValueCommand));

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
		RendererCommandInitializer.InitType<ValueCommand<T>>();
		_valueCallbackMap[id] = callback;
	}

	public void RegisterStringCallback(string id, Action<string> callback)
	{
		RendererCommandInitializer.InitType<StringCommand>();
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

		//RendererCommandInitializer.InitTypes();
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
				var typedMethod = _handleValueCommandMethod.MakeGenericMethod(valueType);
				typedMethod.Invoke(this, new object[] { command });
			}
		}
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

	public void Debug(string msg)
	{
		OnDebug?.Invoke(msg);
	}
}

public static class RendererCommandInitializer
{
	private static MethodInfo _initTypeInnerMethod = AccessTools.Method(typeof(RendererCommandInitializer), "InitTypeInner", []);
	private static HashSet<Type> _initializedTypes = new();
	public static bool IsTypeInitialized(Type type)
	{
		return _initializedTypes.Contains(type);
	}
	public static bool IsTypeInitialized<T>()
	{
		return _initializedTypes.Contains(typeof(T));
	}
	internal static void InitTypes()
	{
		Assembly.GetExecutingAssembly().GetTypes().Where(t => !t.ContainsGenericParameters && t.IsSubclassOf(typeof(RendererCommand))).Do(InitType);
	}
	private static void InitType(Type type)
	{
		if (IsTypeInitialized(type))
			return;

		if (!type.IsSubclassOf(typeof(RendererCommand)))
			throw new Exception($"Type {type.Name} is not a RendererCommand!");

		if (type.ContainsGenericParameters)
			throw new Exception($"Type {type.Name} is not a concrete type!");

		_initTypeInnerMethod.MakeGenericMethod(type).Invoke(null, []);
	}
	public static void InitType<T>()
	{
		var type = typeof(T);
		if (IsTypeInitialized(type))
			return;

		if (!type.IsSubclassOf(typeof(RendererCommand)))
			throw new Exception($"Type {type.Name} is not a RendererCommand!");

		if (type.ContainsGenericParameters)
			throw new Exception($"Type {type.Name} is not a concrete type!");

		InitTypeInner<T>();
	}
	private static void InitTypeInner<T>()
	{
		MessagingHost.Instance!.Debug($"Initializing: {typeof(T).Name} {(typeof(T).IsGenericType ? typeof(T).GetGenericArguments()[0] : "")}");

		var typeToIndex = (Dictionary<Type, int>)AccessTools.Field(typeof(RendererCommand), "typeToIndex").GetValue(null)!;
		var poolBorrowers = (List<Func<IMemoryPackerEntityPool, RendererCommand>>)AccessTools.Field(typeof(RendererCommand), "poolBorrowers").GetValue(null)!;
		var poolReturners = (List<Action<IMemoryPackerEntityPool, RendererCommand>>)AccessTools.Field(typeof(RendererCommand), "poolReturners").GetValue(null)!;

		if (typeToIndex.ContainsKey(typeof(T))) return;

		typeToIndex.Add(typeof(T), typeToIndex.Count);

		MethodInfo method = typeof(PolymorphicMemoryPackableEntity<RendererCommand>).GetMethod("Allocate", BindingFlags.Static | BindingFlags.NonPublic)!;
		MethodInfo method2 = typeof(PolymorphicMemoryPackableEntity<RendererCommand>).GetMethod("Return", BindingFlags.Static | BindingFlags.NonPublic)!;

		MethodInfo methodInfo = method.MakeGenericMethod(typeof(T));
		MethodInfo methodInfo2 = method2.MakeGenericMethod(typeof(T));
		Func<IMemoryPackerEntityPool, RendererCommand> item = (Func<IMemoryPackerEntityPool, RendererCommand>)methodInfo.CreateDelegate(typeof(Func<IMemoryPackerEntityPool, RendererCommand>));
		Action<IMemoryPackerEntityPool, RendererCommand> item2 = (Action<IMemoryPackerEntityPool, RendererCommand>)methodInfo2.CreateDelegate(typeof(Action<IMemoryPackerEntityPool, RendererCommand>));

		poolBorrowers.Add(item);
		poolReturners.Add(item2);

		_initializedTypes.Add(typeof(T));
	}
}

// IMPORTANT:
// RendererCommand derived classes MUST NOT have constructors because it breaks Unity for some reason
// Causes errors in RendererCommandInitializer

public class IdentifiableCommand : RendererCommand
{
	public string Id = "";

	public override void Pack(ref MemoryPacker packer)
	{
		packer.Write(Id);
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		unpacker.Read(ref Id);
	}
}

public class ValueCommand<T> : IdentifiableCommand where T : unmanaged
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

public class StringCommand : IdentifiableCommand
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