using Renderite.Shared;

namespace InterprocessLib;

// This is used as a last resort
// It just creates new objects every time
// Could be improved to actually use pooling

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