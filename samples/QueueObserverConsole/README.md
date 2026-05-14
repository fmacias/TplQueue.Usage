# QueueObserverConsole

`QueueObserverConsole` is the runnable consumer sample for `TPLQ-V1-006`.

It demonstrates:

- one queue dispatcher created through the public `API` facade
- one rooted job graph: `Extract -> Transform -> Load`
- one standalone async helper task enqueued into the same dispatcher outside the job graph
- built-in logging and profiling observers
- deterministic `wait` and `cancel` execution modes that can be exercised manually or by tests

## Run modes

The sample accepts at most one parameter:

```powershell
dotnet run --project samples/QueueObserverConsole/QueueObserverConsole.csproj -- wait
dotnet run --project samples/QueueObserverConsole/QueueObserverConsole.csproj -- cancel
```

If no parameter is provided, the sample defaults to `wait`.

### `wait`

Expected behavior:

- `Extract` reads `Data/greetings.xml`
- `Transform` serializes the greetings document to JSON
- `Load` writes the JSON payload to the console
- the standalone helper task also executes in the same queue dispatcher
- the queue drains and finalizes gracefully

### `cancel`

Expected behavior:

- the sample waits until `Extract` has started
- cancellation is requested while `ExtractAsync` is still inside its simulated delay
- `Extract` is canceled
- `Transform` and `Load` do not run
- the standalone helper task still executes because it was enqueued independently with `CancellationToken.None`
- the queue drains and finalizes gracefully after the cancellation path

## Logs

The sample writes one file per entity under [Logs](Logs):

- `app.log`
- `queue.log`
- `logging-observer.log`
- `profiling-observer.log`

These logs are part of the public sample documentation surface. They make the queue lifecycle and observer behavior inspectable without private source access.
