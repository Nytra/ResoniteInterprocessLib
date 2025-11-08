using Renderite.Shared;

namespace InterprocessLib;

internal class FallbackPool : IMemoryPackerEntityPool
{
	public static IMemoryPackerEntityPool Instance = new FallbackPool();

	T IMemoryPackerEntityPool.Borrow<T>()
	{
		return new T();
	}

	void IMemoryPackerEntityPool.Return<T>(T value)
	{
	}
}