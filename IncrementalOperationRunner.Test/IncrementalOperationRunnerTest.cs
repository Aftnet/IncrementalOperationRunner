using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace IncrementalOperationRunner.Test
{
    public class IncrementalOperationRunnerTest
    {
        //This needs to be long enough to allow all background tasks to complete
        const int TestCompletionWaitingTime = 1000;
        const int BackgroundOperationCancellationCheckStep = 10;

        int BackgroundOperationNumCancellationRequests { get; set; }

        private async Task<int> BackgroundOperationNoCancellation(int input)
        {
            await Task.Delay(input);
            return input;
        }

        private async Task<int> BackgroundOperationWithancellation(int input, IProgress<int> progress, CancellationToken cancellationToken)
        {
            var ctr = 0;
            while (ctr < input)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    BackgroundOperationNumCancellationRequests++;
                }
                cancellationToken.ThrowIfCancellationRequested();

                var progressVal = ctr * 100 / input;
                if (progress != null)
                {
                    progress.Report(progressVal);
                }

                await Task.Delay(BackgroundOperationCancellationCheckStep);
                ctr += BackgroundOperationCancellationCheckStep;
            }

            return input;
        }

        private IncrementalOperationRunner<int, int, int> GetRunner(bool cancellationTokenSupport)
        {
            if (cancellationTokenSupport)
            {
                return new IncrementalOperationRunner<int, int, int>(BackgroundOperationWithancellation);
            }

            return new IncrementalOperationRunner<int, int, int>(BackgroundOperationNoCancellation);
        }

        private IncrementalOperationRunner<int, int, int>[] GetRunners()
        {
            var vals = new bool[] { true, false };
            var output = vals.Select(d => GetRunner(d)).ToArray();
            return output;
        }

        [Fact]
        public async Task No_Events_Defined_Does_Not_Cause_Crash()
        {
            var runner = GetRunner(true);
            runner.Run(10);
            await Task.Delay(TestCompletionWaitingTime);
        }

        [Fact]
        public async Task Operation_Is_Executed_As_Expected()
        {
            var input = 10;
            foreach (var runner in GetRunners())
            {
                var result = 0;
                var numTimesEventHandlerCalled = 0;
                runner.OperationCompletedEvent += d =>
                {
                    result = d;
                    numTimesEventHandlerCalled++;
                };

                Assert.NotEqual(input, result);
                Assert.Equal(0, numTimesEventHandlerCalled);
                runner.Run(input);
                Assert.NotEqual(input, result);
                Assert.Equal(0, numTimesEventHandlerCalled);

                //Simulate idle UI thread
                await Task.Delay(TestCompletionWaitingTime);

                Assert.Equal(input, result);
                Assert.Equal(1, numTimesEventHandlerCalled);
            }
        }

        [Fact]
        public async Task Operations_Called_Before_Previous_Ends_Do_Not_Raise_Completion_Event()
        {
            foreach (var runner in GetRunners())
            {
                var waitTimeBetweenInputs = 30;
                var inputs = new int[] { 2 * waitTimeBetweenInputs, 5 * waitTimeBetweenInputs, 3 * waitTimeBetweenInputs };

                var result = 0;
                var numTimesEventHandlerCalled = 0;
                runner.OperationCompletedEvent += d =>
                {
                    result = d;
                    numTimesEventHandlerCalled++;
                };

                foreach (var i in inputs)
                {
                    Assert.NotEqual(i, result);
                    Assert.Equal(0, numTimesEventHandlerCalled);
                    runner.Run(i);
                    Assert.NotEqual(i, result);
                    Assert.Equal(0, numTimesEventHandlerCalled);
                    await Task.Delay(waitTimeBetweenInputs);
                }

                //Simulate idle UI thread
                await Task.Delay(TestCompletionWaitingTime);

                Assert.Equal(inputs.Last(), result);
                Assert.Equal(1, numTimesEventHandlerCalled);
            }
        }

        [Fact]
        public async Task Operation_Is_Canceled_If_A_New_One_Is_Requested()
        {
            var waitTimeBetweenInputs = 30;
            var inputs = new int[] { 5 * waitTimeBetweenInputs, 5 * waitTimeBetweenInputs, 2 * waitTimeBetweenInputs };

            var runner = GetRunner(true);
            var result = 0;
            var numTimesEventHandlerCalled = 0;
            BackgroundOperationNumCancellationRequests = 0;
            runner.OperationCompletedEvent += d =>
            {
                result = d;
                numTimesEventHandlerCalled++;
            };

            foreach (var i in inputs)
            {
                runner.Run(i);
                await Task.Delay(waitTimeBetweenInputs);
            }

            //Simulate idle UI thread
            await Task.Delay(TestCompletionWaitingTime);

            Assert.Equal(inputs.Last(), result);
            Assert.Equal(1, numTimesEventHandlerCalled);
            Assert.Equal(inputs.Length - 1, BackgroundOperationNumCancellationRequests);
        }

        [Fact]
        public async Task Calling_Stop_Cancels_Operation_Witout_Raising_Completion_event()
        {
            var waitTimeBetweenInputs = 30;
            var initialResultValue = -1;
            var input = 10 * waitTimeBetweenInputs;

            var runner = GetRunner(true);
            var result = initialResultValue;
            var numTimesEventHandlerCalled = 0;
            BackgroundOperationNumCancellationRequests = 0;
            runner.OperationCompletedEvent += d =>
            {
                result = d;
                numTimesEventHandlerCalled++;
            };

            runner.Run(input);
            await Task.Delay(waitTimeBetweenInputs);
            runner.Stop();

            //Simulate idle UI thread
            await Task.Delay(TestCompletionWaitingTime);

            Assert.Equal(initialResultValue, result);
            Assert.Equal(0, numTimesEventHandlerCalled);
            Assert.Equal(1, BackgroundOperationNumCancellationRequests);
        }

        [Fact]
        public async Task Operations_Can_Be_Started_After_Stopping()
        {
            var input = 100;
            foreach (var waitTimeBetweenInputs in new int[] { input / 2, input * 2 })
            {
                var initialResultValue = -1;

                var runner = GetRunner(true);
                var result = initialResultValue;
                var numTimesEventHandlerCalled = 0;
                BackgroundOperationNumCancellationRequests = 0;
                runner.OperationCompletedEvent += d =>
                {
                    result = d;
                    numTimesEventHandlerCalled++;
                };

                runner.Run(input);
                await Task.Delay(waitTimeBetweenInputs);
                runner.Stop();
                await Task.Delay(waitTimeBetweenInputs);
                runner.Run(input);

                //Simulate idle UI thread
                await Task.Delay(TestCompletionWaitingTime);

                Assert.Equal(input, result);
                var operationShouldHaveBeenCanceled = waitTimeBetweenInputs < input;
                var expectedNumCancellations = operationShouldHaveBeenCanceled ? 1 : 0;
                Assert.Equal(expectedNumCancellations, BackgroundOperationNumCancellationRequests);
                var expectedNumCallbacks = operationShouldHaveBeenCanceled ? 1 : 2;
                Assert.Equal(expectedNumCallbacks, numTimesEventHandlerCalled);
            }
        }

        [Fact]
        public async Task Progress_Reporting_Works()
        {
            var runner = GetRunner(true);
            var numTimesEventHandlerCalled = 0;
            runner.ProgressChangedEvent += d =>
            {
                numTimesEventHandlerCalled++;
            };

            var expectedNumProgressCalls = 4;
            var input = expectedNumProgressCalls * BackgroundOperationCancellationCheckStep;
            runner.Run(input);

            //Simulate idle UI thread
            await Task.Delay(2 * input);

            Assert.Equal(expectedNumProgressCalls, numTimesEventHandlerCalled);
        }
    }
}
