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
    public async Task ReadActiveVersion_RetriesThroughATransientLockInsteadOfThrowing()
    {
        var store = new RuntimeActivationStore(root);
        Directory.CreateDirectory(Path.Combine(store.VersionsRoot, "1.0.0"));
        store.Activate("1.0.0");
        var pointerPath = Path.Combine(root, "active.json");

        // A escrita atômica concorrente de outro launcher, ou um antivírus
        // segurando active.json por poucos milissegundos, não pode derrubar a
        // abertura do app: ReadActiveVersion deve tentar de novo em vez de
        // propagar o IOException do lock transitório. O lock fica mais tempo
        // que a espera de um runner de CI mais lento agendar a thread de
        // liberação, mas bem dentro do orçamento de retry de
        // ReadActiveVersion (15 tentativas x 100ms = até 1.4s).
        var handle = new FileStream(pointerPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var releaseTask = Task.Run(async () =>
        {
            await Task.Delay(300);
            handle.Dispose();
        });

        try
        {
            Assert.Equal("1.0.0", store.ReadActiveVersion());
        }
        finally
        {
            // Garante que o lock some antes do Dispose() da fixture apagar o
            // diretório, mesmo se o Assert acima falhar.
            await releaseTask;
        }
    }
}
