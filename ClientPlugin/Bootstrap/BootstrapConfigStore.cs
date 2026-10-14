using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace ClientPlugin.Bootstrap;

public enum RendererBackend { Dx11, Vulkan }

[XmlRoot("VulkanConfig")]
public sealed class BackendConfiguration
{
    public RendererBackend RequestedBackend { get; set; }
    public bool EnableValidation { get; set; }
    public bool ExclusiveFullscreen { get; set; }
    public bool RecoveryRequired { get; set; }
    public long RetryGeneration { get; set; }
    public string LastFailure { get; set; } = "";
    public BackendConfiguration Copy() => new BackendConfiguration
    {
        RequestedBackend = RequestedBackend, EnableValidation = EnableValidation, ExclusiveFullscreen = ExclusiveFullscreen,
        RecoveryRequired = RecoveryRequired, RetryGeneration = RetryGeneration, LastFailure = LastFailure
    };
}

// BCL only: usable after MyFileSystem initialization without loading any game type into the preloader.
public sealed class BootstrapConfigStore
{
    private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(BackendConfiguration));
    private static readonly object SerializerLock = new object();
    public string FilePath { get; }
    public string AttemptPath => FilePath + ".startup";
    public BootstrapConfigStore(string userDataPath)
    {
        if (string.IsNullOrWhiteSpace(userDataPath)) throw new ArgumentException("Game user-data path is not initialized.", nameof(userDataPath));
        FilePath = Path.Combine(userDataPath, "Storage", "Vulkan.cfg");
    }
    public BackendConfiguration Load(out string warning)
    {
        warning = "";
        if (!File.Exists(FilePath)) return new BackendConfiguration();
        try
        {
            var readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 };
            lock (SerializerLock)
            using (var reader = XmlReader.Create(FilePath, readerSettings))
            {
                var config = (BackendConfiguration)Serializer.Deserialize(reader) ?? new BackendConfiguration();
                if (!Enum.IsDefined(typeof(RendererBackend), config.RequestedBackend)) throw new InvalidDataException("Unknown renderer.");
                if (config.RetryGeneration < 0) config.RetryGeneration = 0;
                return config;
            }
        }
        catch (Exception error) when (error is IOException || error is InvalidOperationException || error is XmlException || error is UnauthorizedAccessException)
        {
            warning = "Configuration unreadable; DX11 selected. " + error.Message;
            return new BackendConfiguration { LastFailure = warning };
        }
    }
    public void Save(BackendConfiguration config)
    {
        lock (SerializerLock)
        {
            AtomicWrite(FilePath, stream =>
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true)) Serializer.Serialize(writer, config);
            });
        }
    }
    public bool RecoverIncompleteAttempt(BackendConfiguration config)
    {
        if (!File.Exists(AttemptPath)) return false;
        config.RecoveryRequired = true;
        config.LastFailure = "Previous Vulkan session ended without an orderly renderer shutdown; DX11 recovery is active. Use Retry Vulkan next launch to acknowledge.";
        Save(config);
        return true;
    }
    public void BeginAttempt(long generation) => AtomicWrite(AttemptPath, stream =>
    {
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
            writer.Write(generation.ToString(System.Globalization.CultureInfo.InvariantCulture));
    });
    public void MarkStartupSuccessful() { if (File.Exists(AttemptPath)) File.Delete(AttemptPath); }
    public void Retry(BackendConfiguration config)
    {
        config.RecoveryRequired = false;
        config.RetryGeneration = checked(config.RetryGeneration + 1);
        config.LastFailure = "";
        Save(config);
        MarkStartupSuccessful();
    }
    private static void AtomicWrite(string destination, Action<Stream> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { write(stream); stream.Flush(true); }
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
