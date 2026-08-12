using FiveMCleaner.UpdateRuntime;
using Xunit;

namespace FiveMCleaner.Tests.UpdateRuntime;

public sealed class RuntimeActivationStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FiveMCleanerRuntime", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public void Activate_SwapsOnlyThePointerBetweenStagedVersions()
    {
        var store = new RuntimeActivationStore(root);
        Directory.CreateDirectory(Path.Combine(store.VersionsRoot, "1.0.0"));
        Directory.CreateDirectory(Path.Combine(store.VersionsRoot, "1.1.0"));
        store.Activate("1.0.0");
        store.Activate("1.1.0");
        Assert.Equal("1.1.0", store.ReadActiveVersion());
        Assert.True(Directory.Exists(Path.Combine(store.VersionsRoot, "1.0.0")));
    }

    [Fact]
    public void ReadActiveVersion_RetriesThroughATransientLockInsteadOfThrowing()
    {
        var store = new RuntimeActivationStore(root);
        Directory.CreateDirectory(Path.Combine(store.VersionsRoot, "1.0.0"));
        store.Activate("1.0.0");
        var pointerPath = Path.Combine(root, "active.json");

        // A escrita atômica concorrente de outro launcher, ou um antivírus
        // segurando active.json por poucos milissegundos, não pode derrubar a
        // abertura do app: ReadActiveVersion deve tentar de novo em vez de
        // propagar o IOException do lock transitório. Uma thread dedicada
        // (não o thread pool, que fica sob contenção real com o resto da
        // suíte rodando em paralelo em runners de CI) segura o lock até
        // ReadActiveVersion já estar dentro do laço de retry, comprovado por
        // um ManualResetEventSlim, e só então libera -- sem depender de
        // nenhum tempo fixo de espera.
        using var lockAcquired = new ManualResetEventSlim(false);
        using var releaseLock = new ManualResetEventSlim(false);
        Exception? lockThreadException = null;
        var lockThread = new Thread(() =>
        {
            try
            {
                using var handle = new FileStream(pointerPath, FileMode.Open, FileAccess.Read, FileShare.None);
                lockAcquired.Set();
                releaseLock.Wait();
            }
            catch (Exception exception)
            {
                lockThreadException = exception;
                lockAcquired.Set();
            }
        })
        {
            IsBackground = true
        };
        lockThread.Start();
        lockAcquired.Wait(cancellationToken: global::Xunit.TestContext.Current.CancellationToken);
        Assert.Null(lockThreadException);

        // ReadActiveVersion já está tentando contra o arquivo travado; libera
        // o lock em paralelo enquanto ele retenta, para que a primeira
        // tentativa falhe e uma tentativa seguinte pegue o arquivo livre.
        var releaseThread = new Thread(() =>
        {
            Thread.Sleep(50);
            releaseLock.Set();
        })
        {
            IsBackground = true
        };
        releaseThread.Start();

        try
        {
            Assert.Equal("1.0.0", store.ReadActiveVersion());
        }
        finally
        {
            releaseLock.Set();
            lockThread.Join();
            releaseThread.Join();
        }
    }
}
