using System.Net.Sockets;
using Basin.Diagnostics;
using Tmds.Ssh;

namespace Waylonia.Sessions;

internal sealed class TmdsSshLink : ISshLink
{
    private const int PasswordAttempts = 3;

    private static readonly string[] DefaultKeys = ["id_ed25519", "id_ecdsa", "id_rsa"];

    private static readonly TimeSpan LoginLimit = TimeSpan.FromDays(1);

    private readonly ISshPrompter _prompter;
    private readonly SemaphoreSlim _prompts;
    private readonly BasinLogger _log;
    private readonly TmdsSshLogger _events;
    private readonly SshClient _client;
    private readonly CancellationTokenSource _lost = new();
    private readonly TimeSpan _timeout;
    private readonly string _sshDirectory;
    private bool _connected;
    private bool _disposed;
    private bool _cancelled;
    private bool _timedOut;
    private SshHostKeyState? _rejectedHostKey;

    public TmdsSshLink(string destination, ISshPrompter prompter, SemaphoreSlim prompts, TimeSpan timeout, BasinLogger log, string sshDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(sshDirectory);
        Destination = destination;
        _sshDirectory = sshDirectory;
        _prompter = prompter;
        _prompts = prompts;
        _timeout = timeout;
        _log = log;
        _events = new TmdsSshLogger(log, destination);
        var settings = new SshConfigSettings
        {
            ConfigFilePaths = [.. SshConfigSettings.DefaultConfigFilePaths],
            AutoConnect = false,
            AutoReconnect = false,
            ConnectTimeout = LoginLimit,
            PasswordPrompt = AskPasswordAsync,
            HostAuthentication = ConfirmHostAsync,
            BannerHandler = context => _log.Info($"{destination}: {context.Message.TrimEnd()}"),
            PostConfigure = PostConfigure,
        };
        _client = new SshClient(destination, settings, _events);
    }

    public string Destination { get; }

    public bool IsConnected => _connected && !_lost.IsCancellationRequested;

    public CancellationToken Lost => _lost.Token;

    public async Task ConnectAsync(CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var reaching = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        using var timer = new Timer(
            _ =>
            {
                if (!_events.Authenticating)
                {
                    _timedOut = true;
                    reaching.Cancel();
                }
            },
            null,
            _timeout,
            Timeout.InfiniteTimeSpan);
        try
        {
            await _client.ConnectAsync(reaching.Token).ConfigureAwait(false);
        }
        catch (SshConnectionException error)
        {
            throw Classify(error);
        }
        catch (OperationCanceledException error)
        {
            throw _timedOut
                ? new SshLinkException(
                    SshLinkReason.Unreachable,
                    SshLinkException.Sentence(SshLinkReason.Unreachable, Destination, $"no answer within {_timeout.TotalSeconds:0} seconds"),
                    error)
                : new SshLinkException(SshLinkReason.Cancelled, SshLinkException.Sentence(SshLinkReason.Cancelled, Destination), error);
        }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            throw Classify(error);
        }

        _connected = true;
        _client.Disconnected.Register(() => _lost.Cancel());
    }

    public async Task<ISshListener> ListenUnixAsync(string remotePath, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(remotePath);
        try
        {
            return new TmdsSshListener(await _client.ListenUnixAsync(remotePath, cancellation).ConfigureAwait(false));
        }
        catch (SshChannelException error)
        {
            throw new SshLinkException(
                SshLinkReason.ForwardRefused, SshLinkException.Sentence(SshLinkReason.ForwardRefused, Destination, remotePath), error);
        }
        catch (Exception error) when (error is SshConnectionException or ObjectDisposedException or InvalidOperationException)
        {
            throw new SshLinkException(SshLinkReason.Lost, SshLinkException.Sentence(SshLinkReason.Lost, Destination), error);
        }
    }

    public async Task<ISshCommand> RunAsync(string script, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(script);
        try
        {
            var process = await _client.ExecuteAsync(script, new ExecuteOptions { AllocateTerminal = false }, cancellation).ConfigureAwait(false);
            return new TmdsSshCommand(process);
        }
        catch (Exception error) when (error is SshException or ObjectDisposedException or InvalidOperationException)
        {
            throw new SshLinkException(SshLinkReason.Lost, SshLinkException.Sentence(SshLinkReason.Lost, Destination), error);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _client.Dispose();
        _lost.Cancel();
        return ValueTask.CompletedTask;
    }

    private void PostConfigure(SshConfigSettings.PostConfigureContext context)
    {
        var settings = context.Settings;
        settings.EnableBatchModeWhenConsoleIsRedirected = false;
        if (settings.HostAuthentication is { } configured)
        {
            settings.HostAuthentication = async (hostContext, cancellation) =>
            {
                var accepted = await configured(hostContext, cancellation).ConfigureAwait(false);
                if (!accepted)
                {
                    _rejectedHostKey ??= hostContext.KnownHostResult == KnownHostResult.Unknown ? SshHostKeyState.Unknown : SshHostKeyState.Changed;
                }

                return accepted;
            };
        }

        if (context.IsProxy)
        {
            return;
        }

        var sshDirectory = _sshDirectory;
        foreach (var name in DefaultKeys)
        {
            var path = Path.Combine(sshDirectory, name);
            if (File.Exists(path))
            {
                var destination = $"{settings.UserName}@{settings.HostName}";
                settings.Credentials.Add(new PrivateKeyCredential(path, () => AskPassphrase(destination, path), queryKey: true));
            }
        }
    }

    private string? AskPassphrase(string destination, string path)
    {
        var prompt = new SshSecretPrompt(SshSecretKind.Passphrase, destination, 1, path);
        var answer = SerializedAsync(() => _prompter.AskSecretAsync(prompt, CancellationToken.None)).GetAwaiter().GetResult();
        if (answer is null)
        {
            _cancelled = true;
        }

        return answer;
    }

    private async ValueTask<string?> AskPasswordAsync(PasswordPromptContext context, CancellationToken cancellation)
    {
        if (context.Attempt > PasswordAttempts || _cancelled)
        {
            return null;
        }

        var prompt = new SshSecretPrompt(
            SshSecretKind.Password, $"{context.ConnectionInfo.UserName}@{context.ConnectionInfo.HostName}", context.Attempt);
        var answer = await SerializedAsync(() => _prompter.AskSecretAsync(prompt, cancellation)).ConfigureAwait(false);
        if (answer is null)
        {
            _cancelled = true;
        }

        return answer;
    }

    private async ValueTask<bool> ConfirmHostAsync(HostAuthenticationContext context, CancellationToken cancellation)
    {
        var info = context.ConnectionInfo;
        var key = info.ServerKey.Key;
        switch (context.KnownHostResult)
        {
            case KnownHostResult.Trusted:
                return true;
            case KnownHostResult.Unknown:
                var prompt = new SshHostKeyPrompt(info.HostName, info.Port, key.Type, $"SHA256:{key.SHA256FingerPrint}", SshHostKeyState.Unknown);
                var accepted = await SerializedAsync(() => _prompter.ConfirmHostKeyAsync(prompt, cancellation)).ConfigureAwait(false);
                if (!accepted)
                {
                    _rejectedHostKey = SshHostKeyState.Unknown;
                }

                return accepted;
            default:
                _rejectedHostKey = SshHostKeyState.Changed;
                return false;
        }
    }

    private async Task<T> SerializedAsync<T>(Func<Task<T>> ask)
    {
        await _prompts.WaitAsync().ConfigureAwait(false);
        try
        {
            return await ask().ConfigureAwait(false);
        }
        finally
        {
            _prompts.Release();
        }
    }

    private SshLinkException Classify(Exception error)
    {
        if (_cancelled)
        {
            return Make(SshLinkReason.Cancelled, error);
        }

        if (_rejectedHostKey is { } rejected)
        {
            return Make(rejected == SshHostKeyState.Changed ? SshLinkReason.HostKeyChanged : SshLinkReason.HostKeyRejected, error);
        }

        for (var inner = error; inner is not null; inner = inner.InnerException)
        {
            switch (inner)
            {
                case NotSupportedException unsupported:
                    return new SshLinkException(
                        SshLinkReason.ConfigUnsupported,
                        SshLinkException.Sentence(SshLinkReason.ConfigUnsupported, Destination, SshLinkException.KeywordOf(unsupported.Message)),
                        error);
                case SocketException socket:
                    return new SshLinkException(
                        SshLinkReason.Unreachable, SshLinkException.Sentence(SshLinkReason.Unreachable, Destination, socket.Message), error);
                case TimeoutException:
                    return new SshLinkException(
                        SshLinkReason.Unreachable,
                        SshLinkException.Sentence(SshLinkReason.Unreachable, Destination, $"no answer within {_timeout.TotalSeconds:0} seconds"),
                        error);
            }
        }

        if (_events.Authenticating)
        {
            return new SshLinkException(
                SshLinkReason.AuthenticationFailed,
                SshLinkException.Sentence(SshLinkReason.AuthenticationFailed, Destination, _events.AllowedMethods, ConfigNamesIdentity()),
                error);
        }

        return new SshLinkException(SshLinkReason.Unreachable, SshLinkException.Sentence(SshLinkReason.Unreachable, Destination, error.Message), error);
    }

    private SshLinkException Make(SshLinkReason reason, Exception error) =>
        new(reason, SshLinkException.Sentence(reason, Destination), error);

    private static bool ConfigNamesIdentity()
    {
        foreach (var path in SshConfigSettings.DefaultConfigFilePaths)
        {
            try
            {
                if (File.Exists(path) && File.ReadLines(path).Any(static line => line.TrimStart().StartsWith("IdentityFile", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }

        return false;
    }
}
