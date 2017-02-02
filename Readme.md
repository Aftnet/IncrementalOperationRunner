#Incremental Operation Runner

[![Build status](https://ci.appveyor.com/api/projects/status/ioj46pcr3n40xpt4/branch/master?svg=true)](https://ci.appveyor.com/project/Aftnet/incrementaloperationrunner/branch/master)

Encapsulates the logic needed to perform incremental operations, that is operations for which the invoker is only interested in the output of the most recent input.
Most common use case is incremental search, where the search operation takes time and must be run in the background.

After calling run, a new background task is started to carry out the operation as defined in the background.
Once finished, the operation completed event(s) specified will be called in the same synchronization context as the call to Run.
If run is called again while a background operation is running, a cancellation request is sent to the background task:
in that case, once the task returns another background task running the operation on the current input value is started instead of calling the completion event.
