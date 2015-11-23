using System;
using System.Threading;
using System.Threading.Tasks;

namespace IncrementalOperationRunner
{
    /// <summary>
    /// Encapsulates the logic needed to perform incremental operations, that is operations for which we are only interested in the output of the most recent input.
    /// Most common use case is incremental search, where the search operation takes time and must be run in the background.
    /// After calling run, a new background task is started to carry out the operation as defined in the background.
    /// Once finished, the operation completed event specified will be called in the same synchronization context as the call to run.
    /// If run is called again while a background operation is running, a cancellation request is sent to the background task:
    /// in that case, once the task returns another background task running the operation on the current input value is started instead of calling the completion event.
    /// </summary>
    /// <typeparam name="TInput">Input of the background opertaion</typeparam>
    /// <typeparam name="TOutput">Output of the background operation</typeparam>
    public class IncrementalOperationRunner<TInput, TOutput>
    {
        private readonly Func<TInput, CancellationToken, Task<TOutput>> BackgroundOperation;
        private TInput CurrentInput { get; set; }
        private CancellationTokenSource CTSource { get; set; }
        private bool StopRequested { get; set; }

        public event Action<TOutput> OperationCompletedEvent;

        public IncrementalOperationRunner(Func<TInput, CancellationToken, Task<TOutput>> backgroundOperation)
        {
            BackgroundOperation = backgroundOperation;
        }

        public IncrementalOperationRunner(Func<TInput, Task<TOutput>> backgroundOperation)
        {
            BackgroundOperation = (d, e) => backgroundOperation(d);
        }

        public void Run(TInput input)
        {
            StopRequested = false;
            CurrentInput = input;
            InputChanged();
        }

        public void Stop()
        {
            StopRequested = true;
            if (CTSource != null)
            {
                CTSource.Cancel();
            }
        }

        private async void InputChanged()
        {
            if (CTSource != null)
            {
                CTSource.Cancel();
                return;
            }

            var searchResult = default(TOutput);

            bool shouldContinue = true;
            while (shouldContinue)
            {
                using (CTSource = new CancellationTokenSource())
                {
                    try
                    {
                        searchResult = await BackgroundOperation(CurrentInput, CTSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    shouldContinue = CTSource.IsCancellationRequested && !StopRequested;
                }
                CTSource = null;
            }

            if(!StopRequested)
            {
                OperationCompletedEvent(searchResult);
            }
        }
    }
}
