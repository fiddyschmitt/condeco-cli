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
                };
            }
        }
    }
}
