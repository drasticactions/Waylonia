using System.CommandLine;
using System.CommandLine.Parsing;
using Basin.Diagnostics;

namespace Waylonia.Cli;

public static class CliOptions
{
    private static readonly string[] LogLevelNames = ["trace", "debug", "info", "warn", "error"];

    public static BasinLogLevel ParseLogLevel(string name) => name switch
    {
        "trace" => BasinLogLevel.Trace,
        "debug" => BasinLogLevel.Debug,
        "info" => BasinLogLevel.Info,
        "warn" => BasinLogLevel.Warn,
        "error" => BasinLogLevel.Error,
        _ => throw new ArgumentException($"unknown log level '{name}'", nameof(name)),
    };

    public static Option<string> LogLevel()
    {
        var option = new Option<string>("--log-level")
        {
            Description = $"discard diagnostics below this: {string.Join(", ", LogLevelNames)}",
            HelpName = "LEVEL",
            DefaultValueFactory = _ => BasinDiagnostics.TraceEnabled ? "debug" : "info",
        };

        option.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string?>();
            if (value is not null && !LogLevelNames.Contains(value))
            {
                result.AddError($"unknown log level '{value}' (expected {string.Join(", ", LogLevelNames)})");
            }
        });

        return option;
    }

    public static Option<bool> AllocReport() => new("--alloc-report")
    {
        Description = "report what the run allocated, and whether it collected, on stdout at exit",
    };

    public static Option<long> Frames() => new("--frames")
    {
        Description = "render this many frames and exit, or 0 to run until stopped",
        HelpName = "N",
        DefaultValueFactory = _ => 0L,
    };

    public static Option<string?> Screenshot() => new("--screenshot")
    {
        Description = "write a PNG of the last frame here",
        HelpName = "PNG",
    };

    public static Option<string?> WaypipeListen() => new("--waypipe-listen")
    {
        Description = "bind this endpoint and replay one waypipe channel into the compositor",
        HelpName = "ADDRESS:PORT|PATH",
    };

    public static Option<bool> Gpu() => new("--gpu")
    {
        Description = "advertise dmabuf to channel clients, backing each remote buffer with a host region",
    };

    public static Option<string> Compress()
    {
        var option = new Option<string>("--compress")
        {
            Description = "the channel's compression: lz4, zstd or none. A hard match with the peer.",
            HelpName = "NAME",
            DefaultValueFactory = _ => "lz4",
        };

        option.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<string>() is not ("lz4" or "zstd" or "none"))
            {
                result.AddError("--compress takes lz4, zstd or none");
            }
        });

        return option;
    }

    public static Option<string> Video()
    {
        var option = new Option<string>("--video")
        {
            Description = "decode per-buffer video from the channel peer: h264, vp9, av1 or none. "
                + "',hw' decodes on this host's GPU; ',hwenc'/',swenc' and ',hwdec'/',swdec' say "
                + "where the peer encodes and decodes, and ',bpf=B' the bits per frame it targets.",
            HelpName = "CODEC[,OPTION...]",
            DefaultValueFactory = _ => "none",
        };

        option.Validators.Add(result =>
        {
            if (!VideoChoice.IsValid(result.GetValueOrDefault<string>()))
            {
                result.AddError(
                    "--video takes none, h264, vp9 or av1, each with any of ',hw', "
                    + "',hwenc'/',swenc', ',hwdec'/',swdec' and ',bpf=B', where B is a "
                    + "positive number such as 7.5e5");
            }
        });

        return option;
    }
}
