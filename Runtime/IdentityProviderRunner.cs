using System;
using System.Threading;
using System.Threading.Tasks;

namespace CluefolkSDK {
internal static class IdentityProviderRunner {
  internal const int TimeoutMilliseconds = 15_000;

  internal static async Task<string> Run(Func<Task<string>> provider,
                                         CancellationToken cancellation,
                                         int timeoutMilliseconds =
                                             TimeoutMilliseconds) {
    cancellation.ThrowIfCancellationRequested();
    if (provider == null)
      throw new InvalidOperationException("The identity provider is unavailable.");
    Task<string> providerTask = provider();
    if (providerTask == null)
      throw new InvalidOperationException("The identity provider returned no task.");
    Task delay = Task.Delay(timeoutMilliseconds, cancellation);
    if (await Task.WhenAny(providerTask, delay) != providerTask) {
      // The public provider contract predates cancellation tokens. Observe a
      // late fault without keeping the bridge request alive.
      _ = Observe(providerTask);
      cancellation.ThrowIfCancellationRequested();
      throw new TimeoutException("The identity provider timed out.");
    }
    return await providerTask;
  }

  private static async Task Observe(Task task) {
    try {
      await task;
    } catch {
      // The bridge already completed with timeout/cancellation.
    }
  }
}
}
