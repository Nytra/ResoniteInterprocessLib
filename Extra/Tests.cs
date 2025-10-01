//#define TEST_COMPILATION

using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib.Tests;

public static class Tests
{
	private static Messenger? _messenger;
	private static Messenger? _unknownMessenger;
	private static Action<string>? _logCallback;

	public static void RunTests(Messenger messenger, Messenger unknownMessenger, Action<string> logCallback)
	{
		_messenger = messenger;
		_unknownMessenger = unknownMessenger;
		_logCallback = logCallback;

		TestUnknownMessenger();
		TestUnknownCommandId();
		TestNullString();
		TestEmptyCommand();
		TestString();
		TestValue();
		TestNestedPackable();
		TestCustomRendererCommand();
		TestPackable();
		TestStruct();
		TestNestedStruct();
		TestValueList();
		TestValueHashSet();
		TestStringList();
		TestObjectList();
		TestVanillaObject();

		try
		{
			TestUnregisteredCommand();
		}
		catch (Exception ex) 
		{
			logCallback($"TestUnregisteredCommand threw an exception: {ex.Message}");
		}
		try
		{
			TestUnregisteredPackable();
		}
		catch (Exception ex)
		{
			logCallback($"TestUnregisteredPackable threw an exception: {ex.Message}");
		}
		try
		{
			TestUnregisteredStruct();
		}
		catch (Exception ex)
		{
			logCallback($"TestUnregisteredStruct threw an exception: {ex.Message}");
		}
	}

	static void TestUnknownMessenger()
	{
		_unknownMessenger!.SendEmptyCommand("UnknownMessengerTest");
	}

	static void TestUnknownCommandId()
	{
		_messenger!.SendEmptyCommand("UnknownIdTest");
	}

	static void TestNullString()
	{
		_messenger!.ReceiveString("NullStr", (str) =>
		{
			_logCallback!($"NullStr: {str}");

		});
		_messenger.SendString("NullStr", null!);
	}

	static void TestEmptyCommand()
	{
		_messenger!.ReceiveEmptyCommand("TestEmptyCommand", () =>
		{
			_logCallback!($"TestEmptyCommand");
		});
		_messenger.SendEmptyCommand("TestEmptyCommand");
	}

	static void TestString()
	{
		_messenger!.ReceiveString("TestString", (str) =>
		{
			_logCallback!($"TestString: {str ?? "NULL"}");
		});
		_messenger.SendString("TestString", "I am a test string wow");
	}

	static void TestValue()
	{
		_messenger!.ReceiveValue<int>("TestInt", (val) =>
		{
			_logCallback!($"TestInt: {val}");
		});

		_messenger.SendValue<int>("TestInt", 637);
	}

	static void TestNestedPackable()
	{
		_messenger!.ReceiveObject<TestNestedPackable>("TestNestedPackable", (recvNestedPackable) =>
		{
			_logCallback!($"TestNestedPackable: {recvNestedPackable?.Value}, {recvNestedPackable?.Obj?.ToString() ?? "NULL"}, {recvNestedPackable?.Obj?.Value}, {recvNestedPackable?.Obj?.Text}, {recvNestedPackable?.Obj?.Time}");
		});

		var nestedCmd = new TestCommand();
		nestedCmd.Value = 9999;
		nestedCmd.Text = "I am a nested command!";
		nestedCmd.Time = DateTime.MinValue;

		var testObj = new TestNestedPackable();
		testObj.Value = 0xF8;
		testObj.Obj = nestedCmd;

		_messenger!.SendObject("TestNestedPackable", testObj);
	}

	static void TestCustomRendererCommand()
	{
		_messenger!.ReceiveObject<TestCommand>("TestCustomRendererCommand", (recvCmd) =>
		{
			_logCallback!($"TestCustomRendererCommand: {recvCmd?.Value}, {recvCmd?.Text}, {recvCmd?.Time}");
		});

		var cmd = new TestCommand();
		cmd.Value = 2932;
		cmd.Text = "Hello world";
		cmd.Time = DateTime.Now;
		_messenger.SendObject("TestCustomRendererCommand", cmd);
	}

	static void TestPackable()
	{
		_messenger!.ReceiveObject<TestPackable>("TestPackable", (recvObj) =>
		{
			_logCallback!($"TestPackable: {recvObj?.Value}");
		});

		var obj = new TestPackable();
		obj.Value = 72;
		_messenger.SendObject("TestPackable", obj);
	}

	static void TestStruct()
	{
		_messenger!.ReceiveValue<TestStruct>("TestStruct", (recvStruct) =>
		{
			_logCallback!($"TestStruct: {recvStruct.Value}");
		});

		var testStruct = new TestStruct();
		testStruct.Value = 4;
		_messenger.SendValue("TestStruct", testStruct);
	}

	static void TestNestedStruct()
	{
		_messenger!.ReceiveValue<TestNestedStruct>("TestNestedStruct", (recvNestedStruct) =>
		{
			_logCallback!($"TestNestedStruct: {recvNestedStruct.Nested.Value}");
		});

		var testStruct = new TestStruct();

		var testNestedSruct = new TestNestedStruct();
		testNestedSruct.Nested = testStruct;

		_messenger!.SendValue("TestNestedStruct", testNestedSruct);
	}

	static void TestUnregisteredCommand()
	{
		_messenger!.ReceiveObject<UnregisteredCommand>("UnregisteredCommand", (recv) =>
		{
			_logCallback!($"UnregisteredCommand");
		});

		var unregistered = new UnregisteredCommand();
		_messenger.SendObject("UnregisteredCommand", unregistered);
	}

	static void TestUnregisteredPackable()
	{
		_messenger!.ReceiveValue<UnregisteredPackable>("UnregisteredCommand", (recv) =>
		{
			_logCallback!($"UnregisteredCommand");
		});

		var unregistered = new UnregisteredPackable();
		_messenger.SendValue("UnregisteredCommand", unregistered);
	}

	static void TestUnregisteredStruct()
	{
		_messenger!.ReceiveValue<UnregisteredStruct>("UnregisteredStruct", (recv) =>
		{
			_logCallback!($"UnregisteredStruct");
		});

		var unregistered = new UnregisteredStruct();
		_messenger.SendValue("UnregisteredStruct", unregistered);
	}

	static void TestValueList()
	{
		_messenger!.ReceiveValueList<float>("TestValueList", (list) => 
		{
			_logCallback!($"TestValueList: {list}");
		});

		var list = new List<float>();
		list.Add(2f);
		list.Add(7f);
		list.Add(21f);
		_messenger.SendValueList("TestValueList", list);
	}

	static void TestValueHashSet()
	{
		_messenger!.ReceiveValueHashSet<float>("TestValueHashSet", (list) =>
		{
			_logCallback!($"TestValueHashSet: {list}");
		});

		var set = new HashSet<float>();
		set.Add(99.92f);
		set.Add(127.2f);
		set.Add(-4.32f);
		_messenger.SendValueHashSet("TestValueHashSet", set);
	}

	static void TestObjectList()
	{
		_messenger!.ReceiveObjectList<TestPackable>("TestObjectList", (list) =>
		{
			_logCallback!($"TestObjectList: {list}");
		});

		var list = new List<TestPackable>();
		list.Add(new() { Value = 7 });
		list.Add(new() { Value = 15 });
		list.Add(new() { Value = 83 });
		_messenger.SendObjectList("TestObjectList", list);
	}

	static void TestStringList()
	{
		_messenger!.ReceiveStringList("TestStringList", (list) =>
		{
			_logCallback!($"TestStringList: {list}");
		});

		var list = new List<string>();
		list.Add("Hello");
		list.Add("World");
		list.Add("owo");
		list.Add(null!);
		list.Add("x3");
		_messenger.SendStringList("TestStringList", list);
	}

	static void TestVanillaObject()
	{
		_messenger!.ReceiveObject<RendererInitData>("TestVanillaObject", (recv) =>
		{
			_logCallback!($"TestVanillaObject: {recv.sharedMemoryPrefix} {recv.uniqueSessionId} {recv.mainProcessId} {recv.debugFramePacing} {recv.outputDevice} {recv.setWindowIcon} {recv.splashScreenOverride}");
		});

		var obj = new RendererInitData();
		_messenger.SendObject("TestVanillaObject", obj);
	}

#if TEST_COMPILATION
	//Won't compile
	static void TestInvalidType()
	{
		_messenger!.ReceiveObject<InvalidType>("InvalidType", (recvInvalidType) =>
		{
			_logCallback!($"InvalidType: {recvInvalidType?.Exception}");
		});

		var invalid = new InvalidType();

		invalid.Exception = new Exception();

		_messenger!.SendObject("InvalidType", invalid);
	}

	// Won't compile
	static void TestInvalidStruct()
	{
		_messenger!.ReceiveValue<StructWithObject>("StructWithObject", (recvStructWithObject) =>
		{
			_logCallback!($"StructWithObject: {recvStructWithObject.Assembly}");
		});

		var invalid = new StructWithObject();

		invalid.Assembly = Assembly.GetExecutingAssembly();

		_messenger!.SendValue("StructWithObject", invalid);
	}
#endif
}

public class TestCommand : RendererCommand
{
	public ulong Value;
	public string Text = "";
	public DateTime Time;
	public override void Pack(ref MemoryPacker packer)
	{
		packer.Write(Value);
		packer.Write(Text);
		packer.Write(Time);
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		unpacker.Read(ref Value);
		unpacker.Read(ref Text);
		unpacker.Read(ref Time);
	}
}

public class TestNestedPackable : IMemoryPackable
{
	public byte Value;
	public TestCommand? Obj;

	public void Pack(ref MemoryPacker packer)
	{
		packer.Write(Value);
		packer.WriteObject(Obj);
	}

	public void Unpack(ref MemoryUnpacker unpacker)
	{
		unpacker.Read(ref Value);
		unpacker.ReadObject(ref Obj);
	}
}

public class TestPackable : IMemoryPackable
{
	public uint Value;

	public void Pack(ref MemoryPacker packer)
	{
		packer.Write(Value);
	}

	public void Unpack(ref MemoryUnpacker unpacker)
	{
		unpacker.Read(ref Value);
	}
}

public struct TestStruct
{
	public int Value;
}

public struct TestNestedStruct
{
	public TestStruct Nested;
}

public class InvalidType
{
	public Exception? Exception;
}

public struct StructWithObject
{
	public Assembly Assembly;
}

public struct UnregisteredPackable : IMemoryPackable
{
	public void Pack(ref MemoryPacker packer)
	{
	}

	public void Unpack(ref MemoryUnpacker unpacker)
	{
	}
}

public class UnregisteredCommand : RendererCommand
{
	public override void Pack(ref MemoryPacker packer)
	{
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
	}
}

public struct UnregisteredStruct
{
	public byte Value;
}