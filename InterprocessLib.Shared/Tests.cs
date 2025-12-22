//#define TEST_COMPILATION

using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib.Tests;

public static class Tests
{
	private static Messenger? _messenger;
	private static Action<string>? _logCallback;

	public static void RunTests(Messenger messenger, Action<string> logCallback)
	{
		_messenger = messenger;
		_logCallback = logCallback;

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
		TestStringCollection();
		TestObjectList();
		TestVanillaObject();
		TestVanillaStruct();
		TestVanillaEnum();
		TestValueArray();
		TestObjectArray();
		TestObjectHashSet();
		TestStringArray();
		TestTypeCommand();
	}

	static void TestTypeCommand()
	{
		_messenger!.ReceiveType("TestTypeCommand", (type) =>
		{
			_logCallback!($"TestTypeCommand: {type?.FullName ?? "NULL"}");
		});
		_messenger!.SendType("TestTypeCommand", typeof(Dictionary<LinkedList<float>, float>));
	}

	static void TestValueArray()
	{
		_messenger!.ReceiveValueArray<int>("TestValueArray", (arr) => 
		{
			_logCallback!($"TestValueArray: {string.Join(",", arr!)}");
		});
		var arr = new int[3];
		arr[0] = 4;
		arr[1] = 7;
		arr[2] = -8;
		_messenger.SendValueArray<int>("TestValueArray", arr);
	}

	static void TestObjectArray()
	{
		_messenger!.ReceiveObjectArray<TestCommand?>("TestObjectArray", (arr) =>
		{
			_logCallback!($"TestObjectArray: {string.Join<TestCommand>(",", arr!)}");
		});
		var arr = new TestCommand?[3];
		arr[0] = new TestCommand();
		arr[0]!.Value = 64;
		arr[0]!.Text = "Pizza";
		arr[0]!.Time = DateTime.Now;
		arr[1] = null;
		arr[2] = new TestCommand();
		arr[2]!.Value = 247;
		_messenger.SendObjectArray("TestObjectArray", arr);
	}

	static void TestVanillaStruct()
	{
		_messenger!.ReceiveValue<HapticPointState>("TestVanillaStruct", (val) =>
		{
			_logCallback!($"TestVanillaStruct: {val.force} {val.temperature} {val.pain} {val.vibration}");

		});
		var val = new HapticPointState();
		val.force = 8;
		val.temperature = 4;
		val.pain = 25;
		val.vibration = 12;
		_messenger.SendValue<HapticPointState>("TestVanillaStruct", val);
	}

	static void TestVanillaEnum()
	{
		_messenger!.ReceiveValue<ShadowType>("TestVanillaEnum", (val) =>
		{
			_logCallback!($"TestVanillaEnum: {val}");

		});
		var val = ShadowType.Soft;
		_messenger.SendValue<ShadowType>("TestVanillaEnum", val);
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
		_messenger.SendString("NullStr", null);
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
		_messenger!.ReceiveValue<int>("TestValue", (val) =>
		{
			_logCallback!($"TestValue: {val}");
		});

		_messenger.SendValue<int>("TestValue", 637);
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

	static void TestValueList()
	{
		_messenger!.ReceiveValueCollection<List<float>, float>("TestValueList", (list) => 
		{
			_logCallback!($"TestValueList: {string.Join(",", list!)}");
		});

		var list = new List<float>();
		list.Add(2f);
		list.Add(7f);
		list.Add(21f);
		_messenger.SendValueCollection<List<float>, float>("TestValueList", list);
	}

	static void TestValueHashSet()
	{
		_messenger!.ReceiveValueCollection<HashSet<float>, float>("TestValueHashSet", (list) =>
		{
			_logCallback!($"TestValueHashSet: {string.Join(",", list!)}");
		});

		var set = new HashSet<float>();
		set.Add(99.92f);
		set.Add(127.2f);
		set.Add(-4.32f);
		_messenger.SendValueCollection<HashSet<float>, float>("TestValueHashSet", set);
	}

	static void TestObjectList()
	{
		_messenger!.ReceiveObjectCollection<List<TestPackable>, TestPackable>("TestObjectList", (list) =>
		{
			_logCallback!($"TestObjectList: {string.Join(",", list!)}");
		});

		var list = new List<TestPackable>();
		list.Add(new() { Value = 7 });
		list.Add(new() { Value = 15 });
		list.Add(new() { Value = 83 });
		_messenger.SendObjectCollection<List<TestPackable>, TestPackable>("TestObjectList", list);
	}

	static void TestObjectHashSet()
	{
		_messenger!.ReceiveObjectCollection<HashSet<TestCommand?>, TestCommand?>("TestObjectHashSet", (list) =>
		{
			_logCallback!($"TestObjectHashSet: {string.Join(",", list!)}");
		});

		var set = new HashSet<TestCommand?>();
		set.Add(new TestCommand());
		set.Add(null);
		set.Add(new TestCommand() { Value = 9 });
		_messenger.SendObjectCollection<HashSet<TestCommand?>, TestCommand?>("TestObjectHashSet", set);
	}

	static void TestStringCollection()
	{
		_messenger!.ReceiveStringCollection<List<string>>("TestStringCollection", (list) =>
		{
			_logCallback!($"TestStringCollection: {string.Join(",", list!.Select(s => s ?? "NULL"))}");
		});

		var list = new List<string>();
		list.Add("Hello");
		list.Add("World");
		list.Add("owo");
		list.Add(null!);
		list.Add("x3");
		_messenger.SendStringCollection<List<string>>("TestStringCollection", list);
	}

	static void TestStringArray()
	{
		_messenger!.ReceiveStringArray("TestStringArray", (arr) =>
		{
			_logCallback!($"TestStringArray: {string.Join(",", arr!.Select(s => s ?? "NULL"))}");
		});

		var arr = new string?[]
		{
			"Hello",
			"World",
			"owo",
			null,
			"x3"
		};
		_messenger.SendStringArray("TestStringArray", arr);
	}

	static void TestVanillaObject()
	{
		_messenger!.ReceiveObject<RendererInitData>("TestVanillaObject", (recv) =>
		{
			_logCallback!($"TestVanillaObject: {recv!.sharedMemoryPrefix} {recv.uniqueSessionId} {recv.mainProcessId} {recv.debugFramePacing} {recv.outputDevice} {recv.setWindowIcon} {recv.splashScreenOverride}");
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

	public override string ToString()
	{
		return $"TestCommand: {Value}, {Text}, {Time}";
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

	public override string ToString()
	{
		return $"TestPackable: {Value}";
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