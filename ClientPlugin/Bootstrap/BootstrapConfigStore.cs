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
    // Consecutive Vulkan sessions that ended uncleanly for a reason not traced to the game itself.
    public int UncleanExits { get; set; }
    public bool RendererFaultSeen { get; set; }
    public bool RecoveryPromptPending { get; set; }
    public string FailureFingerprint { get; set; } = "";
    public BackendConfiguration Copy() => new BackendConfiguration
    {
        RequestedBackend = RequestedBackend, EnableValidation = EnableValidation, ExclusiveFullscreen = ExclusiveFullscreen,
        RecoveryRequired = RecoveryRequired, RetryGeneration = RetryGeneration, LastFailure = LastFailure,
        UncleanExits = UncleanExits, RendererFaultSeen = RendererFaultSeen, RecoveryPromptPending = RecoveryPromptPending, FailureFingerprint = FailureFingerprint
    };
    internal void ClearRecovery()
    {
        RecoveryRequired = false; RecoveryPromptPending = false; RendererFaultSeen = false; UncleanExits = 0; FailureFingerprint = ""; LastFailure = "";
    }
}

public enum SessionFault { None, Renderer, Game }

// Active-session journal: deleted on orderly Vulkan shutdown, so its presence at boot means an unclean exit.
public sealed class AttemptJournal
{
    public long Generation { get; set; }
    public bool Verified { get; set; }
    public SessionFault Fault { get; set; }
    public string Detail { get; set; } = "";
    // A fault in our code, or a crash before the first healthy frame, is the renderer's; a verified session without a fault is unknown.
    public bool RendererAttributed => Fault == SessionFault.Renderer || (Fault == SessionFault.None && !Verified);
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
    public const int PromptAfterUncleanExits = 2;
    // Counts the previous session's unclean exit, if any. Game-attributed crashes never count against Vulkan.
    // The second counted exit raises the prompt; it only falls back to DX11 when a counted exit was the renderer's.
    public bool RecoverIncompleteAttempt(BackendConfiguration config)
    {
        var journal = ReadAttempt();
        if (journal == null) return false;
        if (journal.Fault == SessionFault.Game)
            config.LastFailure = "Previous Vulkan session crashed outside the renderer; Vulkan stays active. " + journal.Detail;
        else
        {
            config.UncleanExits++;
            config.RendererFaultSeen |= journal.RendererAttributed;
            string cause = journal.Fault == SessionFault.Renderer ? "a Vulkan renderer fault"
                : journal.Verified ? "an untraced crash" : "a crash during Vulkan startup";
            config.LastFailure = "Previous Vulkan session ended with " + cause + (journal.Detail.Length == 0 ? "" : ": " + journal.Detail)
                + " (" + config.UncleanExits + " in a row).";
            if (config.UncleanExits >= PromptAfterUncleanExits)
            {
                config.RecoveryPromptPending = true;
                config.RecoveryRequired |= config.RendererFaultSeen;
            }
        }
        Save(config);
        File.Delete(AttemptPath);
        return true;
    }
    // A driver, plugin or plugin-set change since the fallback gets Vulkan another try without user action.
    public bool ReleaseChangedRecovery(BackendConfiguration config, string fingerprint)
    {
        if (!config.RecoveryRequired || string.IsNullOrEmpty(fingerprint) || config.FailureFingerprint == fingerprint) return false;
        bool changed = config.FailureFingerprint.Length != 0;
        if (changed) config.ClearRecovery();
        else config.FailureFingerprint = fingerprint;
        if (changed) config.LastFailure = "Graphics driver or plugins changed since the Vulkan fallback; retrying Vulkan.";
        Save(config);
        return changed;
    }
    public AttemptJournal ReadAttempt()
    {
        if (!File.Exists(AttemptPath)) return null;
        var journal = new AttemptJournal();
        foreach (string line in File.ReadAllLines(AttemptPath))
        {
            int split = line.IndexOf('=');
            // Journals from 1.5.0 and earlier hold only the generation number.
            string key = split < 0 ? "generation" : line.Substring(0, split), value = split < 0 ? line : line.Substring(split + 1);
            if (key == "generation") { long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long generation); journal.Generation = generation; }
            else if (key == "verified") journal.Verified = value == "1";
            else if (key == "fault" && Enum.TryParse(value, out SessionFault fault)) journal.Fault = fault;
            else if (key == "detail") journal.Detail = value;
        }
        return journal;
    }
    public void WriteAttempt(AttemptJournal journal) => AtomicWrite(AttemptPath, stream =>
    {
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
        {
            writer.WriteLine("generation=" + journal.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteLine("verified=" + (journal.Verified ? "1" : "0"));
            writer.WriteLine("fault=" + journal.Fault);
            writer.WriteLine("detail=" + (journal.Detail ?? "").Replace('\r', ' ').Replace('\n', ' '));
        }
    });
    public void BeginAttempt(long generation) => WriteAttempt(new AttemptJournal { Generation = generation });
    public void MarkStartupSuccessful() { if (File.Exists(AttemptPath)) File.Delete(AttemptPath); }
    public void Retry(BackendConfiguration config)
    {
        config.ClearRecovery();
        config.RetryGeneration = checked(config.RetryGeneration + 1);
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
