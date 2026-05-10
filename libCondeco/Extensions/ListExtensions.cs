using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Extensions
{
    public static class ListExtensions
    {
        //This achieves 3 things:
        //1. Processes in parallel
        //2. Preserves original order
        //3. Returns items immediately after they are available
        public static IEnumerable<TOutput> SelectParallelPreserveOrder<TInput, TOutput>(this IEnumerable<TInput> list, Func<TInput, TOutput> selector, int? threads = null)
        {
            var resultDictionary = new ConcurrentDictionary<int, TOutput>();

            threads ??= Environment.ProcessorCount;
            threads = Math.Max(threads.Value, 1);

            var outputItems = new BlockingCollection<int>(threads.Value);

            Task.Factory.StartNew(() =>
            {
                list
                    .Select((item, index) => new
                    {
                        Index = index,
                        Value = item
                    })
                    .AsParallel()
                    .WithDegreeOfParallelism(threads.Value)
                    .ForAll(item =>
                    {
                        var result = selector(item.Value);
                        resultDictionary.TryAdd(item.Index, result);
                        outputItems.Add(item.Index);
                    });

                outputItems.CompleteAdding();
            }, TaskCreationOptions.LongRunning);

            var currentIndexToReturn = 0;
            foreach (var _ in outputItems.GetConsumingEnumerable())
            {
                while (resultDictionary.TryGetValue(currentIndexToReturn, out var result))
                {
                    yield return result;

                    resultDictionary.TryRemove(currentIndexToReturn, out var _);
                    currentIndexToReturn++;
                }
                ;
            }
        }

        // TKey combineFn((TKey Key, T Value) prevKeyItem, T curItem)
        //      prevKeyItem.Key = Previous Key (initially, seedKey)
        //      prevKeyItem.Value = Previous Item
        //      curItem = Current Item
        //      returns TKey for Current Item
        public static IEnumerable<(TKey Key, T Value)> ScanToPairs<T, TKey>(this IEnumerable<T> src, TKey seedKey, Func<(TKey Key, T Value), T, TKey> combineFn)
        {
            using var srce = src.GetEnumerator();
            if (srce.MoveNext())
            {
                var prevkv = (seedKey, srce.Current);

                while (srce.MoveNext())
                {
                    yield return prevkv;
                    prevkv = (combineFn(prevkv, srce.Current), srce.Current);
                }
                yield return prevkv;
            }
        }

        // bool testFn(T prevVal, T curVal)
        public static IEnumerable<IGrouping<int, T>> GroupAdjacent<T>(this IEnumerable<T> src, Func<T, T, bool> testFn) =>
            src.ScanToPairs(1, (kvp, cur) => testFn(kvp.Value, cur) ? kvp.Key : kvp.Key + 1)
               .GroupBy(kvp => kvp.Key, kvp => kvp.Value);
    }
}
